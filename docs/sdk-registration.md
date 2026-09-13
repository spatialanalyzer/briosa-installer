# Changing the configured SDK

SDK Setup loads local installation and registration information automatically.
Choose **Change SDK…**, select the SA release whose bundled SDK you want to use,
then review the current version, selected version, and executable path. Close SA,
its SDK, and Briosa servers before proceeding. Choose **Change SDK** and approve
Windows elevation for the installed Hexagon SDK executable.

When a newer bundled SDK is found on the machine, the summary strongly recommends
registering it for most users. This is an advisory: keeping an older SDK is valid
when your workflow requires it. No action is blocked and registration never changes
automatically. Refresh or a completed registration change updates the recommendation.
Unknown or conflicting versions retain their diagnostic findings instead of a
version recommendation; a registered SDK newer than the installed SA releases does
not trigger a downgrade recommendation.

This changes shared COM registration for new SDK sessions. It does not switch an
existing SDK process, choose an SA application instance, or bypass Briosa's runtime
identity checks. Other applications using the shared SDK registration may be
affected. Engineering teams own their application coordination.

## Registration procedure and evidence

The inspected installations 2024.1.0508.5, 2026.1.0529.4, and 2026.1.0529.7 contain
`SpatialAnalyzerSDK-register-server.bat`, which invokes the installed executable
with `/Regserver`. All three procedure files have SHA-256
`EAA2DE87A9DCC7C5A2D4AA917DFE9F7F77DB6DCBB67F550690E0050EB6393390`.
The app invokes that fixed argument on the fully qualified executable; it does
not run the batch file, download a helper, write individual registry values, or
accept commands from a source catalog. Vendor text and binaries are not redistributed.

The SDK executable must have a valid Windows Authenticode signature from one of
these reviewed certificate SHA-256 fingerprints:

- Hexagon Manufacturing Intelligence, Inc. (observed SA 2024):
  `1E1339CA0371DA0A057DAD6C17296E9A232D6E27F1E67C4DCDF690B97E52014B`.
- Hexagon Innovation Hub GmbH (observed SA 2026):
  `84E87961C48124EE3439E6371789937E397214F0093416DA0E986526220DB1EC`.

Certificate renewal or a changed registration procedure requires a reviewed app
update. A matching hash alone is not signature verification. The app checks the
actual signer returned by Windows trust verification, including cached chain
revocation information, with network retrieval disabled. Enterprise administrators
must provision the necessary Windows trust information for offline use.

Windows documents [COM self-registration](https://learn.microsoft.com/en-us/windows/win32/com/self-registration),
[WinVerifyTrust](https://learn.microsoft.com/en-us/windows/win32/api/wintrust/nf-wintrust-winverifytrust),
and [retrieving the verified signer](https://learn.microsoft.com/en-us/windows/win32/api/wintrust/nf-wintrust-wthelpergetprovsignerfromchain).
The installed Hexagon procedure is the evidence for this specific SDK command.

## Checks and recovery

Before mutation, the engine requires a discovered machine installation, matching
SDK file version, a local fixed drive, protected installation permissions, no
reparse points, an approved executable signature, and the reviewed vendor procedure.
It rejects per-user registration overrides, service registration, incomplete
discovery, and detected running SA/SDK/Briosa processes.

The reviewed registration fingerprint and executable hash are rechecked before
launch. A named machine-wide maintenance lock serializes cooperating installers.
External installers and applications do not participate in that lock: keep the
maintenance window clear until verification completes. No process is forcibly
stopped and no registration command is automatically retried.

Pre-change evidence and the selected executable identity are saved in a private
user directory, `%LOCALAPPDATA%\Briosa\Installer\sdk-registration`. A companion
result file records the observed outcome. These local records include paths and
are not included in sanitized support exports. They are evidence for a reviewed
restoration, not a complete Windows registry backup or an executable rollback file.
If writing the result file fails, the original pre-change record remains and the
actual mutation result is still reported.

Success requires both a successful vendor exit and a fresh effective registration
matching the selected SDK path and version in the inspecting user's context,
with machine registration evidence. A nonzero exit, unreadable/mismatched
post-state, or timeout is not success. After 60 seconds without process completion,
the app reports an unknown outcome and does not terminate the vendor process.
Do not retry while it is running. Refresh after it exits and review the actual
registration. To restore a prior SDK, choose that still-installed release and
review a new vendor registration change; never blindly import old registry data.

## CLI

The CLI uses the same validation, fixed vendor command, maintenance lock, and
verification. It works without a package source:

```powershell
$cli = '.\Briosa.Installer.Cli.exe'
$installation = 'C:\Program Files (x86)\New River Kinematics\SpatialAnalyzer <release>'
$review = (& $cli sdk plan --installation $installation | ConvertFrom-Json)
# Review the returned plan before proceeding.
& $cli sdk use --installation $installation --review-sha256 $review.reviewSha256 --yes
```

Exit codes: 0 verified success; 2 invalid arguments; 6 rejected preflight or changed
review; 7 failed/unverified/unknown vendor outcome; 130 Windows elevation cancelled.
Once the vendor command starts, cancelling a CLI caller is not a safe rollback.

Ordinary tests use fake registration environments and do not touch the actual
Windows SDK registration. Live registration validation is separately authorized
and recorded in the design QA notes. It does not constitute runtime MP validation
or establish a broader supported SA-release matrix.
