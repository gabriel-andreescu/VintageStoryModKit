set_xmakever("3.1.1")
set_project("VintageStoryModKit")

add_repositories("vsmk " .. path.join(os.scriptdir(), "..", ".."))
add_addons("vsmk 0.1.0")
includes("@addon/vsmk/project")

target("VintageStoryModKit", function()
    add_rules("@addon/vsmk/mod", { project = "VintageStoryModKit.csproj" })
    add_installfiles("modinfo.json")
    add_installfiles("../../LICENSE", { prefixdir = "licenses", filename = "VintageStoryModKit.txt" })
    add_installfiles("../../licenses/Humanizer.txt", { prefixdir = "licenses" })
    add_installfiles("../../licenses/json-everything.txt", { prefixdir = "licenses" })
end)
