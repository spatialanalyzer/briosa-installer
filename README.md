# Briosa Installer

One Windows management application for installing and maintaining Briosa
distributions for the exact SpatialAnalyzer releases your projects use.

**Status: catalog-browsing development preview.** The .NET 10/WPF application and
CLI share source settings, read catalogs from HTTPS or local/share paths, and
preview server packages and installer releases through their configured sources.
Catalog entries are unverified metadata; the app does not acquire payloads,
install packages, apply updates, or interact with SpatialAnalyzer. There is no
released installer. The broader features below remain proposals.

See [build and run instructions](docs/development.md), the
[offline example walkthrough](examples/README.md), and the
[catalog implementation boundary](docs/architecture/0002-release-catalog-browsing.md).

## Proposed experience

Install the standard management application once. It defaults to the Briosa
public release source; engineers can point the same application at an enterprise
mirror through its GUI or an editable configuration file. A customized installer
or IT-prepared configuration is not required. It should help engineers:

- Find installed SpatialAnalyzer releases and their approved Briosa packages.
- Install several exact-target server distributions side by side.
- Inspect installed server versions and their exact SA targets.
- Review updates and install new versions alongside existing packages.
- Diagnose SDK setup and request or perform approved maintenance when needed.
- Work entirely through internal repositories such as JFrog Artifactory, or use
  complete offline bundles.

Every running Briosa server remains dedicated to one exact SA application
release. Installing several products does not enable concurrent automation of
arbitrary SA windows. Installing a package and proving an execution session ready
are separate actions.

The installer and server do not maintain an inventory of consuming applications,
project profiles, or client dependencies. Engineering teams own their application
configuration and change-impact assessment through their own processes and tools.
The installer tracks its packages, source configuration, and maintenance results.

## Design documents

| Document | Purpose |
| --- | --- |
| [Application experience](docs/proposals/application-experience.md) | Screens, interaction design, everyday workflows, and first-release scope. |
| [Source configuration](docs/proposals/source-configuration.md) | Default public source, engineer-configured mirrors, shared GUI/file/script settings, and optional administrator policy. |
| [Installer and SDK management](docs/proposals/installer-and-sdk-management.md) | Enterprise distribution, package management, SDK compatibility, registration repair, and recovery. |
| [Administrator and engineer workflows](docs/proposals/installer-user-workflows.md) | How engineers configure sources, install server versions, review SDK setup, and maintain packages. |
| [Community discussion](https://github.com/orgs/spatialanalyzer/discussions/8) | Published product proposal and implementation questions; the [initial post](docs/discussions/installer-product-proposal.md) is retained here. |

The management and workflow proposals originated during
[Briosa #158](https://github.com/spatialanalyzer/briosa/issues/158) planning and
have moved here. Their proposed SDK policy depends on separately reviewed runtime
changes: current server identity and compatibility checks remain authoritative.

## Enterprise distribution

Internal sources and offline deployment are first-release requirements. The
standard application must let an engineer select an enterprise source before
any public metadata, package, or update request. The GUI, direct file editing,
and scripts use one versioned configuration format. Installer updates use the
server-package source by default, with a visible optional override for a separate
installer-update catalog. Each source supplies both metadata and payloads.

An organization can optionally supply defaults, enforce policy, or deploy the
same installer through its software portal. Those controls are separate from
basic mirror configuration. The complete standard installer must also be usable
without public access, including its prerequisites.

The proposed installer consumes immutable server artifacts and catalogs from an
approved file/HTTPS feed, including an Artifactory generic repository. Client
libraries and their dependencies continue to use the organization's NuGet, PyPI,
and npm repositories. A blocked internal source must produce a useful diagnostic
without falling back to a public source.

## Repository boundaries

| Repository | Owns |
| --- | --- |
| `briosa-installer` | Installer GUI/CLI, deployment policy integration, package acquisition, installed package inventory, and approved SDK maintenance orchestration. |
| [briosa](https://github.com/spatialanalyzer/briosa) | Exact-target server/worker products, public protocol, artifact and shared client/runtime contracts, runtime identity, readiness, and SDK compatibility enforcement. |
| [briosa-dotnet](https://github.com/spatialanalyzer/briosa-dotnet), [briosa-py](https://github.com/spatialanalyzer/briosa-py), [briosa-js](https://github.com/spatialanalyzer/briosa-js) | Idiomatic target-specific client libraries and their package-manager integration. |
| [briosa-docs](https://github.com/spatialanalyzer/briosa-docs) | Published end-user documentation for released behavior. |
| [community](https://github.com/spatialanalyzer/community) | Cross-project architecture and product discussions. |
| [governance](https://github.com/spatialanalyzer/governance) | Organization policies and stewardship. |

The installer has an independent version and release lifecycle. It consumes
published products and metadata; it must not embed the server's MP implementation
or redefine SDK compatibility in the UI. An optional future runtime monitor is
separate from the package-management application and is not required to execute
an engineering workflow.

## Contributing and planning

See [CONTRIBUTING.md](CONTRIBUTING.md) and [AGENTS.md](AGENTS.md). Planned work is
tracked in GitHub issues and the
[Briosa Roadmap & Delivery project](https://github.com/orgs/spatialanalyzer/projects/1).
Start design discussion in
[organization Discussions](https://github.com/orgs/spatialanalyzer/discussions).

The first implementation uses .NET 10 and WPF, with a shared engine and CLI.
Windows packaging, signing, enterprise authentication, and the public catalog
remain implementation decisions. The development build requires .NET 10;
complete offline deployment remains a release requirement.

## License and product relationship

Briosa is an independent open-source project developed with support from Hexagon.
It is not an official Hexagon product and does not imply certification or
guaranteed compatibility. SpatialAnalyzer and the SA SDK remain Hexagon products;
a separately installed and licensed SpatialAnalyzer environment is required for
useful MP execution. This repository does not distribute those proprietary
products or grant rights to their trademarks.

Repository source and documentation are licensed under Apache-2.0. See
[LICENSE](LICENSE) and the organization's
[brand guidance](https://github.com/spatialanalyzer/governance/blob/main/BRAND_AND_TRADEMARKS.md).
