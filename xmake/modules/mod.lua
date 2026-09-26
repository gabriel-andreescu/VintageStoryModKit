import("core.project.config")

function build(target)
    local dotnet = import("@self.dotnet")
    local options = target:data("vsmk.mod")
    local project = path.absolute(assert(options.project, "Set the C# project path."), os.projectdir())
    local directory = path.join(config.builddir(), "vsmk", (target:fullname():gsub("::", "/")))
    local stage = path.join(directory, "stage")
    local arguments = {
        "build",
        project,
        "--target:VsmkStage",
        "--configuration:" .. (options.configuration or "Release"),
        "--property:VsmkStagePath=" .. path.absolute(stage),
        "--nologo",
    }
    table.join2(arguments, dotnet.game_arguments())
    os.vrunv(dotnet.program(), arguments)
    return import("@self.payload").prepare(target, stage, path.join(directory, "payload"))
end
