// Starts the API, takes its OpenAPI document, and writes it next to the web application.
//
// The document is committed, and a CI job runs this again and fails if the result differs. That is
// what makes a renamed field on the server break the frontend build rather than a browser at runtime.
//
// The host is started with --no-launch-profile on purpose: launchSettings.json overrides the address
// from the environment, and the first version of this script spent its time polling a port nobody was
// listening on.
import { spawn } from "node:child_process";
import { mkdirSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const port = process.env.OPENAPI_PORT ?? "5199";
const output = join(root, "src", "Pitchwire.Web", "openapi.json");

const api = spawn(
  "dotnet",
  ["run", "--project", "src/Pitchwire.Api", "--no-launch-profile", "--urls", `http://127.0.0.1:${port}`],
  {
    cwd: root,
    stdio: "ignore",
    shell: process.platform === "win32",
    env: {
      ...process.env,
      // Enough to start. Nothing here reaches a database: generating the document only reads routes.
      ConnectionStrings__Postgres: "Host=127.0.0.1;Port=1;Database=pitchwire;Username=none;Password=none",
      Ingest__Secret: "contract-generation-only",
      Seed__Enabled: "false",
      ASPNETCORE_ENVIRONMENT: "Development",
    },
  },
);

const stop = () => {
  api.kill();

  if (process.platform === "win32") {
    // dotnet run starts the host as a child, and killing the launcher leaves it holding the port.
    spawn("taskkill", ["/F", "/IM", "Pitchwire.Api.exe"], { stdio: "ignore", shell: true });
  }
};

try {
  let document = null;

  for (let attempt = 0; attempt < 60 && document === null; attempt++) {
    await new Promise((resolve) => setTimeout(resolve, 1000));

    try {
      const response = await fetch(`http://127.0.0.1:${port}/openapi/v1.json`);

      if (response.ok) {
        document = await response.json();
      }
    } catch {
      // Not up yet.
    }
  }

  if (document === null) {
    console.error(`The API did not serve its OpenAPI document on port ${port}.`);
    process.exitCode = 1;
  } else {
    mkdirSync(dirname(output), { recursive: true });
    writeFileSync(output, `${JSON.stringify(document, null, 2)}\n`);
    console.log(`Wrote ${Object.keys(document.paths).length} paths to ${output}`);
  }
} finally {
  stop();
}
