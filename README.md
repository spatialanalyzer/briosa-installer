# Briosa Installer

One Windows management application for installing and maintaining Briosa
distributions for the exact SpatialAnalyzer releases your projects use.

**Status: design and repository setup.** This repository does not yet contain an
installer implementation, downloadable application, or released CLI. The features
below are proposals, not current product capabilities.

## Proposed experience

Install the management application once through your organization's software
portal. It should help engineers:

- Find installed SpatialAnalyzer releases and their approved Briosa packages.
- Install several exact-target server distributions side by side.
- Pin each project's server and compatible client selection in a profile.
- Review updates and change a project's selection for its next session.
- Diagnose SDK setup and request or perform approved maintenance when needed.
- Work entirely through internal repositories such as JFrog Artifactory, or use
  complete offline bundles.

Every running Briosa server remains dedicated to one exact SA application
release. Installing several products does not enable concurrent automation of
arbitrary SA windows. Installing a package and proving an execution session ready
are separate actions.

## Design documents

| Document | Purpose |
| --- | --- |
| [Application experience](docs/proposals/application-experience.md) | Screens, interaction design, everyday workflows, and first-release scope. |
| [Installer and SDK management](docs/proposals/installer-and-sdk-management.md) | Enterprise distribution, package management, SDK compatibility, registration repair, and recovery. |
| [Administrator and engineer workflows](docs/proposals/installer-user-workflows.md) | How IT prepares an approved source and engineers install, switch targets, update, and recover. |
| [Community discussion draft](docs/discussions/installer-product-proposal.md) | A review-ready proposal and focused questions for a dedicated community discussion. Not yet posted. |

The management and workflow proposals originated during
[Briosa #158](https://github.com/spatialanalyzer/briosa/issues/158) planning and
have moved here. Their proposed SDK policy depends on separately reviewed runtime
changes: current server identity and compatibility checks remain authoritative.

## Enterprise distribution

Internal sources and offline deployment are first-release requirements. In
managed mode, source selection, trusted publishers, approved versions, and repair
permissions come from IT policy. Public network access must not be necessary for
bootstrap, package installation, metadata, prerequisites, or updates.

The proposed installer consumes immutable server artifacts and catalogs from an
approved file/HTTPS feed, including an Artifactory generic repository. Client
libraries and their dependencies continue to use the organization's NuGet, PyPI,
and npm repositories. A blocked internal source must produce a useful diagnostic
without falling back to a public source.

## Repository boundaries

| Repository | Owns |
| --- | --- |
| `briosa-installer` | Installer GUI/CLI, deployment policy integration, package acquisition, local package/profile management, and approved SDK maintenance orchestration. |
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

There is no build or test command yet. Implementation should establish a shared
installer engine with a noninteractive CLI and Windows GUI; the UI framework,
packaging technology, and signing arrangement remain open decisions.

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
