using System.Reflection;
using System.Text.Json.Nodes;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using JsonObject = System.Text.Json.Nodes.JsonObject;

namespace VintageStoryModKit.Settings;

public sealed class SettingsHost : IDisposable
{
    private const string SchemaResourceName = "VintageStoryModKit.Settings.Schema";
    private readonly ICoreAPI api;
    private readonly SettingsStore store;
    private readonly string cacheKey;
    private readonly bool ownsFile;
    private readonly IServerNetworkChannel? serverChannel;
    private readonly IClientNetworkChannel? clientChannel;
    private readonly long tickListener;
    private readonly string[] integrationEvents = [];
    private readonly EventBusListenerDelegate integrationListener;
    private bool disposed;

    private SettingsHost(ICoreAPI api, ModInfo mod, SettingsSchema schema)
    {
        this.api = api;
        string modId = mod.ModID;
        if (
            mod.Side != EnumAppSide.Universal
            && (mod.Side == EnumAppSide.Client) != (schema.Side == SettingsSide.Client)
        )
        {
            throw new InvalidOperationException(
                $"Mod '{modId}' runs only on the {mod.Side.ToString().ToLowerInvariant()}, but its settings belong to the {schema.Side.ToString().ToLowerInvariant()}. Set x-vsmk.side to {mod.Side} in the settings schema."
            );
        }

        string file = schema.ConfigFile(modId);
        cacheKey = $"vsmk:settings:{file}";
        if (api.ObjectCache.ContainsKey(cacheKey))
        {
            throw new InvalidOperationException(
                $"Settings file '{file}' already has a VSMK host on this side."
            );
        }

        ownsFile = (schema.Side == SettingsSide.Client) == (api.Side == EnumAppSide.Client);
        store = new SettingsStore(schema, Path.Combine(api.DataBasePath, "ModConfig", file));
        IsReady = ownsFile;
        if (ownsFile)
        {
            Reload();
        }

        if (schema.Side == SettingsSide.Server && mod.Side == EnumAppSide.Universal)
        {
            string channel = $"vsmk:{modId}:{file}";
            if (api is ICoreServerAPI server)
            {
                serverChannel = server
                    .Network.RegisterChannel(channel)
                    .RegisterMessageType<SettingsSnapshot>();
                server.Event.PlayerNowPlaying += SendToPlayer;
            }
            else if (api is ICoreClientAPI client)
            {
                clientChannel = client
                    .Network.RegisterChannel(channel)
                    .RegisterMessageType<SettingsSnapshot>();
                clientChannel.SetMessageHandler<SettingsSnapshot>(ReceiveSnapshot);
            }
        }

        integrationListener = OnIntegrationChange;
        if (ownsFile)
        {
            tickListener = api.Event.RegisterGameTickListener(_ => Reload(true), 2000);
            integrationEvents =
            [
                $"imm.{modId}",
                $"configkit:{modId}:setting-changed",
                $"configkit:{modId}:config-saved",
                $"configlib:{modId}:setting-changed",
                $"configlib:{modId}:config-saved",
            ];
            foreach (string name in integrationEvents)
            {
                api.Event.RegisterEventBusListener(integrationListener, filterByEventName: name);
            }
        }
        api.ObjectCache[cacheKey] = this;
    }

    public static SettingsHost Open(
        ICoreAPI api,
        ModSystem modSystem,
        string resourceName = SchemaResourceName
    ) => Open(api, modSystem.Mod.Info, modSystem.GetType().Assembly, resourceName);

    public static SettingsHost Open(
        ICoreAPI api,
        ModInfo mod,
        Assembly assembly,
        string resourceName = SchemaResourceName
    )
    {
        using Stream stream =
            assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded settings schema '{resourceName}' was not found in {assembly.GetName().Name}."
            );
        using var reader = new StreamReader(stream);
        return new SettingsHost(api, mod, SettingsSchema.Parse(reader.ReadToEnd()));
    }

    public bool IsReady { get; private set; }
    public JsonObject Current => store.Current;
    public event Action? Changed;

    public T Get<T>(string path) => store.Get<T>(path);

    public T Snapshot<T>() => store.Snapshot<T>();

    public void Reload() => Reload(false);

    public void Save(JsonObject value) => Commit(() => store.Save(value));

    public void Save<T>(T values) => Commit(() => store.Save(values));

    private void Commit(Func<bool> save)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!ownsFile)
        {
            throw new InvalidOperationException(
                "Only the owning game side can save these settings."
            );
        }

        if (save())
        {
            OnChanged();
        }
    }

    private void Reload(bool onlyIfChanged)
    {
        if (disposed || !ownsFile)
        {
            return;
        }

        bool changed;
        try
        {
            changed = store.Reload(onlyIfChanged);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or System.Text.Json.JsonException
                        or SettingsValidationException
            )
        {
            api.Logger.Error(
                "Unable to load settings '{0}'. Keeping the last valid values. {1}",
                store.FilePath,
                exception.Message
            );
            return;
        }
        if (changed)
        {
            OnChanged();
        }
    }

    private void OnIntegrationChange(string name, ref EnumHandling handling, IAttribute data) =>
        Reload(true);

    private void OnChanged()
    {
        serverChannel?.BroadcastPacket(
            new SettingsSnapshot { Json = store.Current.ToJsonString() }
        );
        Changed?.Invoke();
    }

    private void SendToPlayer(IServerPlayer player) =>
        serverChannel!.SendPacket(
            new SettingsSnapshot { Json = store.Current.ToJsonString() },
            player
        );

    private void ReceiveSnapshot(SettingsSnapshot snapshot)
    {
        bool changed;
        try
        {
            JsonObject values =
                JsonNode.Parse(snapshot.Json) as JsonObject
                ?? throw new System.Text.Json.JsonException(
                    "Settings snapshot must contain an object."
                );
            changed = store.ApplySnapshot(values);
        }
        catch (Exception exception)
            when (exception is System.Text.Json.JsonException or SettingsValidationException)
        {
            api.Logger.Error("Unable to apply server settings: {0}", exception.Message);
            return;
        }
        bool wasReady = IsReady;
        IsReady = true;
        if (changed || !wasReady)
        {
            Changed?.Invoke();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (ownsFile)
        {
            api.Event.UnregisterGameTickListener(tickListener);
            // The game removes one matching registration per call.
            foreach (string _ in integrationEvents)
            {
                api.Event.UnregisterEventBusListener(integrationListener);
            }
        }
        if (api is ICoreServerAPI server && serverChannel is not null)
        {
            server.Event.PlayerNowPlaying -= SendToPlayer;
        }

        clientChannel?.SetMessageHandler<SettingsSnapshot>(_ => { });
        api.ObjectCache.Remove(cacheKey);
        Changed = null;
    }
}

[ProtoContract]
internal sealed class SettingsSnapshot
{
    [ProtoMember(1)]
    public string Json { get; set; } = "{}";
}
