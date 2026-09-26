import("core.project.config", { alias = "project_config" })
import("core.base.json")
import("utils.archive")

local function filename(value)
    assert(
        type(value) == "string"
            and value ~= ""
            and value ~= "."
            and value ~= ".."
            and not value:find('[<>:"/\\|?*]')
            and not value:find("[. ]$"),
        "Invalid package filename: " .. tostring(value)
    )
    return value
end

function package(target, payload, options)
    local components = target:fullname():split("::", { plain = true })
    for _, component in ipairs(components) do
        filename(component)
    end
    local dist = path.absolute(project_config.get("distdir") or path.join(project_config.builddir(), "dist"))
    local output = path.join(dist, table.concat(components, "/"))
    local manifest =
        path.join(os.projectdir(), ".xmake/vsmk/packages", hash.uuid(target:fullname() .. "\n" .. output) .. ".lua")
    local metadata = path.join(os.projectdir(), ".xmake/vsmk/packages", hash.uuid(target:fullname()) .. ".json")
    local previous = os.isfile(manifest) and io.load(manifest) or {}
    local modinfo_file = path.join(payload, "modinfo.json")
    assert(os.isfile(modinfo_file), "The package has no modinfo.json. Declare it with add_installfiles.")
    local modinfo = json.loadfile(modinfo_file)
    local version = assert(modinfo.version, "Set version in modinfo.json.")
    local name = filename(
        (options.package_name or assert(modinfo.modid, "Set modid in modinfo.json.")) .. "-" .. version .. ".zip"
    )
    local destination = path.join(output, name)
    assert(not os.isdir(destination), "Package output is a directory: " .. destination)
    assert(
        not os.isfile(destination) or previous[name:lower()],
        "Packaging would overwrite an unowned file: " .. destination
    )
    local current = { [name:lower()] = name }
    local files = os.files(path.join(payload, "**"))
    for index, file in ipairs(files) do
        files[index] = path.relative(file, payload)
    end
    local temporary = os.tmpfile() .. ".zip"
    try({
        function()
            archive.archive(temporary, files, { curdir = payload, compress = "best" })
            -- Retain both generations until replacement succeeds so retries own partial output.
            local pending = table.copy(previous)
            for key, file in pairs(current) do
                pending[key] = file
            end
            io.save(manifest, pending)
            os.mkdir(output)
            os.mv(temporary, destination)
            for key, file in pairs(previous) do
                local obsolete = path.join(output, filename(file))
                if not current[key] and os.isfile(obsolete) then
                    os.rm(obsolete)
                end
            end
            io.save(manifest, current)
            json.savefile(metadata, {
                target = target:fullname(),
                name = options.package_name or modinfo.modid,
                version = version,
                archive = path.relative(destination, dist):gsub("\\", "/"),
                changelog = options.changelog
                    and path.relative(path.absolute(options.changelog, os.projectdir()), os.projectdir())
                        :gsub("\\", "/"),
            })
        end,
        finally({
            function(ok, errors)
                os.tryrm(temporary)
                if not ok then
                    raise(errors)
                end
            end,
        }),
    })
end
