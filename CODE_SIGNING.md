# Code signing setup and proposed policy

Status: preparation only. No application to the SignPath Foundation has been submitted or approved, and no trusted signing certificate has been provisioned. The workflow does not make existing downloads signed. Acceptance into the free program is decided by the Foundation; this repository makes no claim of sponsorship or endorsement.

The proposed provider is the [SignPath Foundation](https://signpath.org/), which offers free code signing to accepted open-source projects. Dolly Paste is published under the [MIT License](LICENSE). Its source and build scripts are public; it uses only the Windows .NET Framework runtime. GitHub-hosted Windows runners build from the checked-out source and run the existing test suites before requesting a signature.

If accepted and activated, free code signing will be provided by [SignPath.io](https://signpath.io/), with a certificate provided by the [SignPath Foundation](https://signpath.org/). This attribution is conditional on acceptance and does not describe the current unsigned downloads.

## Proposed signing policy

- Maintainer, reviewer and release approver: [sander1993s](https://github.com/sander1993s). Changes to release code and signing configuration must be reviewed before signing.
- Release signing requires an explicit manual dispatch, approval of the `release-signing` GitHub environment, and manual approval in SignPath after reviewing the source commit and build results. Automatic approval must remain disabled for the release signing policy.
- The maintainer and anyone later given repository administration or signing access must enable multi-factor authentication on GitHub and SignPath before access is granted.
- The signing service must restrict requests to this repository, the reviewed `main` branch and GitHub-hosted runners. The submitter token must only submit requests; it must not approve them.
- Only the Dolly Paste executable built from this repository is signed. Third-party Windows components are neither bundled nor re-signed.
- Dolly Paste does not transmit clipboard contents or other user information to network systems. Settings remain local; history stays in memory unless the user enables local DPAPI-encrypted persistence. See the [storage and privacy model](README.md#storage-and-privacy-model).

These are the required controls for activating the prepared integration, not a statement that external account settings have already been configured. Protect the repository's `main` branch and review changes before release. Update this document after acceptance with the approved provider details and any required attribution.

## Activate after approval

1. Complete the Foundation application and identity/project review. Use the approved project and certificate details; do not buy a paid plan as part of this setup. SignPath's certificate subject determines the publisher shown by Windows and may name the Foundation instead of Smet Software Solutions.
2. Configure SignPath's GitHub trusted build integration for `sander1993s/Dolly_Paste`. Install its GitHub App with access to this repository as required by the approved setup. Apply `.signpath/artifact-configuration.xml` to the artifact configuration. It accepts a GitHub artifact ZIP containing `Dolly Paste.exe` with the expected product, company and version metadata.
3. Require manual approval by `sander1993s` in the SignPath release signing policy, enable MFA, restrict source/build origin as described above, and use the approved public-trust certificate with Authenticode timestamping. Keep the API token limited to submission.
4. Create the GitHub environment `release-signing`, allow deployment only from `main`, add required reviewer `sander1993s`, and disable administrator bypass. The same maintainer currently initiates and reviews releases, so do not enable prevention of self-review unless another authorized reviewer is added. GitHub cannot enforce SignPath MFA or approval from this YAML; verify those controls in the service before enabling signing.
5. Add the environment secret `SIGNPATH_API_TOKEN`. Add these environment variables using the values from the approved configuration:

   | Variable | Value |
   | --- | --- |
   | `SIGNPATH_ORGANIZATION_ID` | SignPath organization ID |
   | `SIGNPATH_PROJECT_SLUG` | Dolly Paste project slug |
   | `SIGNPATH_SIGNING_POLICY_SLUG` | Manually approved public-trust release policy slug |
   | `SIGNPATH_ARTIFACT_CONFIGURATION_SLUG` | Configuration created from the XML in this repository |
   | `SIGNPATH_EXPECTED_SIGNER_SUBJECT` | Exact full certificate Subject string, independently confirmed from the approved certificate |
   | `SIGNPATH_ENABLED` | Set to `true` only after all controls above are configured |

   Obtain the expected signer subject from the approved certificate before the first run. Do not copy it blindly from an unexpected signing result. It is public certificate metadata, not a private key. Tokens and private keys must never be committed.

## Prepare a signed download

1. Update `src/AssemblyInfo.cs` and `app.manifest` together when changing the four-part version. The initial metadata matches the existing manifest version `1.0.0.0`.
2. Review and merge the intended source, then manually run **Prepare signed Windows download** from `main` as `sander1993s`. Approve the environment after checking the selected commit.
3. Review and approve the corresponding SignPath signing request within 30 minutes. The workflow fails if configuration, build, tests, approval, signature, timestamp or publisher validation fails; there is no unsigned fallback.
4. Download the `Dolly-Paste-Windows-signed-<version>` artifact from the successful run. The enclosed `dolly-paste-windows.zip` contains exactly `Dolly Paste.exe`, `LICENSE` and a bilingual `README.txt` with the version, publisher, source commit and executable SHA256. Only this verified ZIP is intended for distribution. The separate `UNSIGNED-signing-input-NOT-FOR-DISTRIBUTION` artifact is required by SignPath's provenance checks, expires after one day and must never be published.
5. Inspect the executable's **Properties > Digital Signatures** and manually publish the verified ZIP to the website. This workflow does not create a release or change website downloads. Update website signing claims only after the signed download is actually published.

`scripts/New-SignedPackage.ps1` checks `Get-AuthenticodeSignature` status `Valid`, embedded signature type `Authenticode`, the exact approved signer subject, a timestamp certificate, and the app's product/version metadata before packaging. A fresh `dist/release` directory is required to exclude stale output. The `.exe` inside the ZIP is signed; the ZIP itself is not an Authenticode executable.

Trusted signing identifies the publisher. Microsoft Defender SmartScreen may still warn until a new release or publisher develops sufficient reputation.

References: [Foundation terms](https://signpath.org/terms), [SignPath GitHub integration](https://docs.signpath.io/trusted-build-systems/github), [artifact configuration](https://docs.signpath.io/artifact-configuration/), [Microsoft SmartScreen reputation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation).
