function run(target)
    local dotnet = import("@self.dotnet")
    local options = target:data("vsmk.test")
    local arguments = {
        "test",
        path.absolute(assert(options.project, "Set the test project path."), os.projectdir()),
        "--configuration:" .. (options.configuration or "Release"),
        "--nologo",
    }
    table.join2(arguments, dotnet.game_arguments())
    return os.execv(dotnet.program(), arguments, { try = true }) == 0
end
