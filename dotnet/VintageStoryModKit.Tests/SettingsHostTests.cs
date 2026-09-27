using System.Text.Json.Nodes;
using NSubstitute;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using VintageStoryModKit.Settings;

namespace VintageStoryModKit.Tests;

public sealed class SettingsHostTests : IDisposable
{
    private const string ModId = "hostfixture";
    private readonly string directory = Path.Combine(
        Environment.GetEnvironmentVariable("VSMK_TEST_ROOT")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "scratch"),
        "host-" + Guid.NewGuid().ToString("N")
    );

    [Theory]
    [InlineData(EnumAppSide.Universal)]
    [InlineData(EnumAppSide.Server)]
    public void ServerCreatesCompleteDefaultsBeforeRegisteringIntegrationListeners(
        EnumAppSide modSide
    )
    {
        ServerContext context = CreateServer();
        string path = ConfigPath();
        bool? defaultsExistedAtRegistration = null;
        context
            .Events.When(events =>
                events.RegisterEventBusListener(
                    Arg.Any<EventBusListenerDelegate>(),
                    Arg.Any<double>(),
                    Arg.Any<string>()
                )
            )
            .Do(_ => defaultsExistedAtRegistration = File.Exists(path));

        using var host = SettingsHost.Open(
            context.Api,
            new ModInfo { ModID = ModId, Side = modSide },
            typeof(SettingsHostTests).Assembly
        );

        Assert.True(host.IsReady);
        Assert.True(defaultsExistedAtRegistration);
        JsonObject persisted = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.True(persisted["Enabled"]!.GetValue<bool>());
        Assert.Equal(5, persisted["Tuning"]!["Count"]!.GetValue<int>());
        Assert.Equal(
            [
                "imm.hostfixture",
                "configkit:hostfixture:setting-changed",
                "configkit:hostfixture:config-saved",
                "configlib:hostfixture:setting-changed",
                "configlib:hostfixture:config-saved",
            ],
            context.IntegrationNames
        );
        Assert.Empty(context.Broadcasts);
        if (modSide == EnumAppSide.Server)
        {
            context.Api.Network.DidNotReceive().RegisterChannel(Arg.Any<string>());
        }
    }

    [Fact]
    public void ServerBroadcastsAcceptedReloadsAndKeepsLastValidStateAfterInvalidEdits()
    {
        ServerContext context = CreateServer();
        using SettingsHost host = Open(context.Api);
        int changes = 0;
        host.Changed += () => changes++;

        File.WriteAllText(ConfigPath(), """{"Enabled":false,"Tuning":{"Count":8}}""");
        context.Tick!(0);

        Assert.False(host.Get<bool>("Enabled"));
        Assert.Equal(8, host.Get<int>("/Tuning/Count"));
        Assert.Equal(1, changes);
        SettingsSnapshot broadcast = Assert.Single(context.Broadcasts);
        Assert.False(JsonNode.Parse(broadcast.Json)!["Enabled"]!.GetValue<bool>());

        const string invalid = "{\"Enabled\":\"invalid\",\"Tuning\":{\"Count\":8}}";
        File.WriteAllText(ConfigPath(), invalid);
        context.Tick(0);

        Assert.False(host.Get<bool>("Enabled"));
        Assert.Equal(8, host.Get<int>("/Tuning/Count"));
        Assert.Equal(invalid, File.ReadAllText(ConfigPath()));
        Assert.Equal(1, changes);
        Assert.Single(context.Broadcasts);
    }

    [Fact]
    public void ClientWaitsForValidServerSnapshotAndNeverCreatesOrWritesTheServerFile()
    {
        ClientContext context = CreateClient();
        using SettingsHost host = Open(context.Api);
        int changes = 0;
        host.Changed += () => changes++;

        Assert.False(host.IsReady);
        Assert.False(File.Exists(ConfigPath()));
        Assert.Throws<InvalidOperationException>(() => host.Save(new JsonObject()));

        context.Handler!(
            new SettingsSnapshot { Json = """{"Enabled":true,"Tuning":{"Count":-1}}""" }
        );

        Assert.False(host.IsReady);
        Assert.True(host.Get<bool>("Enabled"));
        Assert.Equal(5, host.Get<int>("/Tuning/Count"));
        Assert.Equal(0, changes);
        Assert.False(File.Exists(ConfigPath()));

        context.Handler(
            new SettingsSnapshot { Json = """{"Enabled":true,"Tuning":{"Count":5}}""" }
        );

        Assert.True(host.IsReady);
        Assert.True(host.Get<bool>("Enabled"));
        Assert.Equal(5, host.Get<int>("/Tuning/Count"));
        Assert.Equal(1, changes);
        Assert.False(File.Exists(ConfigPath()));

        context.Handler!(
            new SettingsSnapshot { Json = """{"Enabled":false,"Tuning":{"Count":9}}""" }
        );

        Assert.True(host.IsReady);
        Assert.False(host.Get<bool>("Enabled"));
        Assert.Equal(9, host.Get<int>("/Tuning/Count"));
        Assert.Equal(2, changes);
        Assert.False(File.Exists(ConfigPath()));

        context.Handler(
            new SettingsSnapshot { Json = """{"Enabled":true,"Tuning":{"Count":-1}}""" }
        );

        Assert.True(host.IsReady);
        Assert.False(host.Get<bool>("Enabled"));
        Assert.Equal(9, host.Get<int>("/Tuning/Count"));
        Assert.Equal(2, changes);
        Assert.False(File.Exists(ConfigPath()));
    }

    [Fact]
    public void ClientOwnedSettingsAreReadyAndPersistLocallyWithoutNetworking()
    {
        ClientContext context = CreateClient();
        using var host = SettingsHost.Open(
            context.Api,
            new ModInfo { ModID = ModId, Side = EnumAppSide.Client },
            typeof(SettingsHostTests).Assembly,
            "VintageStoryModKit.ClientSettingsSchema"
        );

        Assert.True(host.IsReady);
        Assert.True(File.Exists(ConfigPath()));
        Assert.Null(context.Handler);
        context.Network.DidNotReceive().RegisterChannel(Arg.Any<string>());

        host.Save(JsonNode.Parse("""{"Enabled":false,"Tuning":{"Count":7}}""")!.AsObject());

        Assert.False(host.Get<bool>("Enabled"));
        Assert.Equal(7, host.Get<int>("/Tuning/Count"));
        JsonObject persisted = JsonNode.Parse(File.ReadAllText(ConfigPath()))!.AsObject();
        Assert.False(persisted["Enabled"]!.GetValue<bool>());
        Assert.Equal(7, persisted["Tuning"]!["Count"]!.GetValue<int>());
    }

    [Fact]
    public void DisposeUnregistersLifecycleHooksAndStopsCapturedCallbacks()
    {
        ServerContext context = CreateServer();
        SettingsHost host = Open(context.Api);
        Action<float> tick = context.Tick!;
        EventBusListenerDelegate integration = context.IntegrationListeners[0];
        IServerPlayer player = null!;

        context.Events.PlayerNowPlaying += Raise.Event<PlayerDelegate>(player);
        Assert.Single(context.Sends);

        host.Dispose();

        context.Events.Received(1).UnregisterGameTickListener(context.TickId);
        context.Events.Received(5).UnregisterEventBusListener(integration);
        Assert.DoesNotContain("vsmk:settings:hostfixture.json", context.Api.ObjectCache.Keys);

        File.WriteAllText(ConfigPath(), """{"Enabled":false,"Tuning":{"Count":12}}""");
        tick(0);
        EnumHandling handling = EnumHandling.PassThrough;
        integration("imm.hostfixture", ref handling, null!);
        context.Events.PlayerNowPlaying += Raise.Event<PlayerDelegate>(player);

        Assert.True(host.Get<bool>("Enabled"));
        Assert.Empty(context.Broadcasts);
        Assert.Single(context.Sends);
    }

    [Theory]
    [InlineData(EnumAppSide.Client, "VintageStoryModKit.SettingsSchema")]
    [InlineData(EnumAppSide.Server, "VintageStoryModKit.ClientSettingsSchema")]
    public void OneSidedModsRejectSettingsOwnedByTheOtherSide(EnumAppSide modSide, string resource)
    {
        ICoreAPI api = modSide == EnumAppSide.Client ? CreateClient().Api : CreateServer().Api;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            SettingsHost.Open(
                api,
                new ModInfo { ModID = ModId, Side = modSide },
                typeof(SettingsHostTests).Assembly,
                resource
            )
        );

        Assert.Contains("x-vsmk.side", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(ConfigPath()));
    }

    private static SettingsHost Open(ICoreAPI api) =>
        SettingsHost.Open(api, new ModInfo { ModID = ModId }, typeof(SettingsHostTests).Assembly);

    private string ConfigPath() => Path.Combine(directory, "ModConfig", ModId + ".json");

    private ServerContext CreateServer()
    {
        ICoreServerAPI api = Substitute.For<ICoreServerAPI>();
        IServerEventAPI events = Substitute.For<IServerEventAPI>();
        IServerNetworkAPI network = Substitute.For<IServerNetworkAPI>();
        IServerNetworkChannel channel = Substitute.For<IServerNetworkChannel>();
        ILogger logger = Substitute.For<ILogger>();
        Dictionary<string, object> cache = [];
        List<SettingsSnapshot> broadcasts = [];
        List<SettingsSnapshot> sends = [];
        List<EventBusListenerDelegate> integrationListeners = [];
        List<string> integrationNames = [];
        Action<float>? tick = null;
        const long tickId = 73;

        api.Side.Returns(EnumAppSide.Server);
        api.DataBasePath.Returns(directory);
        api.ObjectCache.Returns(cache);
        api.Logger.Returns(logger);
        api.Event.Returns(events);
        ((ICoreAPI)api).Event.Returns(events);
        api.Network.Returns(network);
        ((ICoreAPI)api).Network.Returns(network);
        network.RegisterChannel(Arg.Any<string>()).Returns(channel);
        channel.RegisterMessageType<SettingsSnapshot>().Returns(channel);
        channel
            .When(value =>
                value.BroadcastPacket(Arg.Any<SettingsSnapshot>(), Arg.Any<IServerPlayer[]>())
            )
            .Do(call => broadcasts.Add(call.Arg<SettingsSnapshot>()));
        channel
            .When(value =>
                value.SendPacket(Arg.Any<SettingsSnapshot>(), Arg.Any<IServerPlayer[]>())
            )
            .Do(call =>
            {
                Assert.Single(call.Arg<IServerPlayer[]>());
                sends.Add(call.Arg<SettingsSnapshot>());
            });
        events
            .RegisterGameTickListener(Arg.Any<Action<float>>(), 2000, 0)
            .Returns(call =>
            {
                tick = call.Arg<Action<float>>();
                return tickId;
            });
        events
            .When(value =>
                value.RegisterEventBusListener(
                    Arg.Any<EventBusListenerDelegate>(),
                    Arg.Any<double>(),
                    Arg.Any<string>()
                )
            )
            .Do(call =>
            {
                integrationListeners.Add(call.Arg<EventBusListenerDelegate>());
                integrationNames.Add(call.ArgAt<string>(2));
            });

        return new ServerContext(
            api,
            events,
            broadcasts,
            sends,
            integrationListeners,
            integrationNames,
            () => tick,
            tickId
        );
    }

    private ClientContext CreateClient()
    {
        ICoreClientAPI api = Substitute.For<ICoreClientAPI>();
        IClientEventAPI events = Substitute.For<IClientEventAPI>();
        IClientNetworkAPI network = Substitute.For<IClientNetworkAPI>();
        IClientNetworkChannel channel = Substitute.For<IClientNetworkChannel>();
        ILogger logger = Substitute.For<ILogger>();
        Dictionary<string, object> cache = [];
        NetworkServerMessageHandler<SettingsSnapshot>? handler = null;

        api.Side.Returns(EnumAppSide.Client);
        api.DataBasePath.Returns(directory);
        api.ObjectCache.Returns(cache);
        api.Logger.Returns(logger);
        api.Event.Returns(events);
        ((ICoreAPI)api).Event.Returns(events);
        api.Network.Returns(network);
        ((ICoreAPI)api).Network.Returns(network);
        network.RegisterChannel(Arg.Any<string>()).Returns(channel);
        channel.RegisterMessageType<SettingsSnapshot>().Returns(channel);
        channel
            .SetMessageHandler(Arg.Any<NetworkServerMessageHandler<SettingsSnapshot>>())
            .Returns(call =>
            {
                handler = call.Arg<NetworkServerMessageHandler<SettingsSnapshot>>();
                return channel;
            });

        return new ClientContext(api, network, () => handler);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed record ServerContext(
        ICoreServerAPI Api,
        IServerEventAPI Events,
        List<SettingsSnapshot> Broadcasts,
        List<SettingsSnapshot> Sends,
        List<EventBusListenerDelegate> IntegrationListeners,
        List<string> IntegrationNames,
        Func<Action<float>?> TickAccessor,
        long TickId
    )
    {
        public Action<float>? Tick => TickAccessor();
    }

    private sealed record ClientContext(
        ICoreClientAPI Api,
        IClientNetworkAPI Network,
        Func<NetworkServerMessageHandler<SettingsSnapshot>?> HandlerAccessor
    )
    {
        public NetworkServerMessageHandler<SettingsSnapshot>? Handler => HandlerAccessor();
    }
}
