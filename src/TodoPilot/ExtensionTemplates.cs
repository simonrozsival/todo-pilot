namespace TodoPilot;

public static class ExtensionTemplates
{
    public static string CreateExtensionScript(string version, InstallScope scope)
    {
        var scopeName = scope == InstallScope.User ? "user" : "project";
        return $$"""
            import { joinSession } from "@github/copilot-sdk/extension";
            import { basename, join } from "node:path";
            import { homedir } from "node:os";
            import { existsSync, mkdirSync, renameSync, rmSync, writeFileSync } from "node:fs";

            const EXTENSION_NAME = "todo-pilot";
            const EXTENSION_VERSION = "{{JavaScriptString.Encode(version)}}";
            const INSTALL_SCOPE = "{{scopeName}}";
            const COPILOT_DIR = join(homedir(), ".copilot");
            const REGISTRY_DIR = join(COPILOT_DIR, EXTENSION_NAME, "sessions");
            const HEARTBEAT_MS = 5000;

            function now() {
                return new Date().toISOString();
            }

            function inferSessionId(session) {
                if (typeof session?.workspacePath !== "string") {
                    return null;
                }

                const name = basename(session.workspacePath);
                return /^[a-f0-9-]{32,40}$/i.test(name) ? name : null;
            }

            function atomicWrite(path, text) {
                mkdirSync(REGISTRY_DIR, { recursive: true });
                const tmp = `${path}.${process.pid}.${Date.now()}.tmp`;
                writeFileSync(tmp, text, "utf-8");
                renameSync(tmp, path);
            }

            const session = await joinSession({ tools: [], hooks: {} });
            const sessionId = inferSessionId(session);

            if (!sessionId) {
                await session.log("todo-pilot could not infer the current Copilot session id.", { level: "warning" });
            } else {
                const registryPath = join(REGISTRY_DIR, `${sessionId}.json`);
                const startedAt = now();

                function entry(status) {
                    return {
                        sessionId,
                        workspacePath: session.workspacePath,
                        cwd: process.cwd(),
                        scope: INSTALL_SCOPE,
                        pid: process.pid,
                        startedAt,
                        lastSeen: now(),
                        status,
                        version: EXTENSION_VERSION
                    };
                }

                function writeStatus(status = "active") {
                    atomicWrite(registryPath, JSON.stringify(entry(status), null, 2));
                }

                writeStatus();

                const heartbeat = setInterval(() => {
                    try {
                        writeStatus();
                    } catch (error) {
                        session.log(`todo-pilot heartbeat failed: ${error.message}`, { level: "warning", ephemeral: true }).catch(() => {});
                    }
                }, HEARTBEAT_MS);
                heartbeat.unref?.();

                session.on("session.shutdown", () => {
                    try {
                        writeStatus("shutdown");
                    } catch {}
                });

                const markStopped = () => {
                    try {
                        if (existsSync(registryPath)) {
                            writeStatus("stopped");
                        }
                    } catch {}
                };

                process.on("exit", markStopped);
                process.on("SIGTERM", () => { markStopped(); process.exit(0); });
                process.on("SIGINT", () => { markStopped(); process.exit(0); });
            }
            """;
    }
}

internal static class JavaScriptString
{
    public static string Encode(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
}
