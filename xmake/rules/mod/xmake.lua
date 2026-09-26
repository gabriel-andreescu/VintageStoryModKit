rule("mod")
on_load(function(target)
    target:set("kind", "phony")
    target:data_set("vsmk.mod", target:extraconf("rules", "@addon/vsmk/mod") or {})
end)
after_load(function(target)
    for _, name in ipairs(target:data("vsmk.mod").targets or {}) do
        target:add("deps", name, { inherit = false })
    end
end)
on_build(function(target)
    local payload = import("@self.mod").build(target)
    target:data_set("vsmk.payload", payload)
    if import("core.project.config").get("deploy") then
        import("@self.deployment").deploy(target, payload)
    end
end)
on_package(function(target)
    local payload = target:data("vsmk.payload") or import("@self.mod").build(target)
    import("@self.packaging").package(target, payload, target:data("vsmk.mod"))
end)
