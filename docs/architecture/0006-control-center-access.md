# Control Center Access

An installed server with `Briosa.ControlCenter.exe` in its receipt exposes **Open
Control Center** in Installations. The shared package engine rechecks store
recovery, installed publisher policy, complete file membership, and receipt
hashes before resolving this fixed executable. The GUI starts it hidden with
the sole `--show` argument. Catalogs cannot supply a launch command or arguments.

This is an explicit desktop navigation action. Installation stays inert; opening
the UI does not start the API host or SDK. The Control Center owns subsequent
runtime controls, connection readiness, and server ownership. The installer
neither attaches an SDK client nor monitors command execution.

Legacy packages omit the action. An inaccessible or modified package fails
verification. The existing trusted local store boundary applies; verification
does not isolate execution from a malicious process running as the same user.

Runtime source of truth: [Briosa Control Center design](https://github.com/spatialanalyzer/briosa/blob/181-windows-control-center/targets/2026.1.0529.7/docs/architecture/desktop-control-center.md).
Delivery: #13 and spatialanalyzer/briosa#181. Tests use signed inert fixtures and
never execute fixture launchers or activate SpatialAnalyzer.
