import("core.project.config")
import("lib.detect.find_tool")

function program()
    return assert(find_tool("dotnet"), "Install the project's .NET SDK.").program
end

function game_arguments()
    local game_path = config.get("game_path")
    if game_path and game_path:trim() ~= "" then
        return { "--property:VsmkGamePath=" .. path.absolute(game_path, os.projectdir()) }
    end
    return {}
end
