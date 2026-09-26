local function staged(stage, select)
    for _, line in ipairs(io.readfile(path.join(stage, ".vsmk-files")):split("\n", { plain = true })) do
        local relative = (line:trim():gsub("\\", "/"))
        if relative ~= "" then
            select(path.join(stage, relative), relative)
        end
    end
end

local function installed(source_target, output, select)
    local sources, destinations = source_target:installfiles(output)
    for index, source in ipairs(sources) do
        assert(os.isfile(source), "Package input is missing: " .. source)
        select(source, (path.relative(destinations[index], output):gsub("\\", "/")))
    end
end

local function collect(target, stage, output)
    local selected = {}
    local function select(source, relative)
        if path.filename(relative):lower() ~= ".gitkeep" then
            selected[relative:lower()] = { source = source, relative = relative }
        end
    end
    staged(stage, select)
    for _, name in ipairs(target:data("vsmk.mod").targets or {}) do
        installed(assert(target:dep(name), "Unknown package target: " .. name), output, select)
    end
    installed(target, output, select)
    local files = table.values(selected)
    table.sort(files, function(left, right)
        return left.relative:lower() < right.relative:lower()
    end)
    return files
end

function prepare(target, stage, output)
    local files = collect(target, stage, output)
    local temporary = os.tmpfile() .. ".dir"
    os.mkdir(temporary)
    try({
        function()
            for _, file in ipairs(files) do
                os.cp(file.source, path.join(temporary, file.relative))
            end
            if os.exists(output) then
                os.rm(output)
            end
            os.mkdir(path.directory(output))
            os.mv(temporary, output)
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
    return output
end
