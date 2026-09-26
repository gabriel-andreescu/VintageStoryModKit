import("core.base.json")

local function key(file)
    return path.normalize(path.absolute(file)):gsub("\\", "/"):lower()
end

local function parents(file, visit)
    local directory = path.directory(file)
    while directory and directory ~= file do
        if visit(directory) == false then
            break
        end
        file, directory = directory, path.directory(directory)
    end
end

local function record_files(record)
    local files = {}
    local root = key(record.destination):gsub("/+$", "")
    for relative in pairs(record.files) do
        local absolute = key(path.join(record.destination, relative))
        assert(absolute:startswith(root .. "/"), "Invalid deployment manifest path: " .. relative)
        files[absolute] = path.join(record.destination, relative)
    end
    return files
end

local function preflight(plan, other_files, other_directories)
    local previous = record_files(plan.previous)
    local checked_owners, checked_paths = {}, {}
    local function check_owner(file)
        local owner = other_files[file] or other_directories[file]
        parents(file, function(parent)
            if checked_owners[parent] then
                return false
            end
            owner = owner or other_files[parent]
            checked_owners[parent] = true
            if owner then
                return false
            end
        end)
        assert(
            not owner,
            "Deployment conflict at " .. file .. " between targets " .. plan.target .. " and " .. tostring(owner)
        )
    end
    for file in pairs(previous) do
        check_owner(file)
    end
    for file in pairs(plan.files) do
        check_owner(file)
        if os.isfile(file) then
            assert(previous[file], "Deployment would overwrite an unowned file: " .. file)
        elseif os.isdir(file) then
            for _, child in ipairs(os.files(path.join(file, "**"))) do
                assert(previous[key(child)], "Deployment would remove an unowned file: " .. child)
            end
        end
        parents(file, function(parent)
            if checked_paths[parent] then
                return false
            end
            assert(not os.isfile(parent) or previous[parent], "Deployment is blocked by an unowned file: " .. parent)
            checked_paths[parent] = true
        end)
    end
end

local function apply(plan)
    local previous = record_files(plan.previous)
    local directories = {}
    local root = key(plan.destination):gsub("/+$", "")
    for file, original in pairs(previous) do
        if not plan.files[file] then
            if os.isfile(original) then
                os.rm(original)
            end
            parents(file, function(parent)
                if directories[parent] or not parent:startswith(root .. "/") then
                    return false
                end
                directories[parent] = true
            end)
        end
    end
    for file in pairs(plan.files) do
        if os.isdir(file) then
            directories[file] = true
            for _, directory in ipairs(os.dirs(path.join(file, "**"))) do
                directories[key(directory)] = true
            end
        end
    end
    local ordered = table.keys(directories)
    table.sort(ordered, function(left, right)
        return #left > #right
    end)
    for _, directory in ipairs(ordered) do
        if
            os.isdir(directory)
            and #os.files(path.join(directory, "*")) == 0
            and #os.dirs(path.join(directory, "*")) == 0
        then
            os.rmdir(directory)
        end
    end
    local files = table.keys(plan.files)
    table.sort(files)
    for _, name in ipairs(files) do
        local file = plan.files[name]
        os.cp(file.source, file.destination, { copy_if_different = true })
    end
end

function deploy(target, payload)
    local settings = path.join(os.projectdir(), ".xmake", "vsmk", "deploy.json")
    if not os.isfile(settings) then
        return
    end
    local destinations = json.loadfile(settings)[target:fullname()]
    if destinations == nil then
        return
    end
    assert(
        type(destinations) == "table" and (table.is_array(destinations) or table.empty(destinations)),
        "Deployment destinations must be an array for target " .. target:fullname()
    )
    if #destinations == 0 then
        return
    end

    local manifest = path.join(os.projectdir(), ".vsmk", "deployment.lua")
    local records = os.isfile(manifest) and io.load(manifest) or {}
    local plans, active = {}, {}
    for _, destination in ipairs(destinations) do
        assert(
            type(destination) == "string" and destination:trim() ~= "",
            "Deployment destinations must be nonempty paths"
        )
        destination = path.normalize(path.absolute(destination, os.projectdir()))
        local id = hash.uuid(target:fullname() .. "\n" .. key(destination))
        if not active[id] then
            active[id] = true
            local current, files = {}, {}
            for _, source in ipairs(os.files(path.join(payload, "**"))) do
                local relative = path.relative(source, payload)
                current[relative] = true
                local output = path.join(destination, relative)
                files[key(output)] = { source = source, destination = output }
            end
            table.insert(plans, {
                id = id,
                target = target:fullname(),
                destination = destination,
                files = files,
                current = current,
                previous = records[id] or { destination = destination, files = {} },
            })
        end
    end

    local other_files, other_directories = {}, {}
    for id, record in pairs(records) do
        if not active[id] then
            for file in pairs(record_files(record)) do
                other_files[file] = record.target
                parents(file, function(parent)
                    if other_directories[parent] then
                        return false
                    end
                    other_directories[parent] = record.target
                end)
            end
        end
    end
    for _, plan in ipairs(plans) do
        preflight(plan, other_files, other_directories)
        for file in pairs(plan.files) do
            other_files[file] = plan.target
            parents(file, function(parent)
                if other_directories[parent] then
                    return false
                end
                other_directories[parent] = plan.target
            end)
        end
    end
    for _, plan in ipairs(plans) do
        -- Retain both generations until copying succeeds so retries own partial output.
        local pending = table.copy(plan.previous.files)
        for relative in pairs(plan.current) do
            pending[relative] = true
        end
        records[plan.id] = { target = plan.target, destination = plan.destination, files = pending }
        io.save(manifest, records)
        apply(plan)
        records[plan.id] = { target = plan.target, destination = plan.destination, files = plan.current }
        io.save(manifest, records)
    end
end
