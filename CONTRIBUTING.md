# Contributing

Thanks for helping make Briosa straightforward to deploy and maintain.

## Before opening a pull request

1. Start from a focused GitHub issue and coordinate planned work through the
   [Briosa Roadmap & Delivery project](https://github.com/orgs/spatialanalyzer/projects/1).
2. Read [AGENTS.md](AGENTS.md) and the relevant [design proposal](README.md#design-documents).
3. Create a short-lived branch named `<issue-number>-<short-description>`.
4. Keep the change coherent and reviewable. Describe the user-visible outcome,
   validation performed, and remaining limitations.
5. Use `Closes #<issue-number>` only when the change satisfies the issue; use
   `Refs #<issue-number>` for partial work. Qualify cross-repository references.

There is no executable implementation or test suite yet. For documentation
changes, verify links, run `git diff --check`, and confirm that proposals cannot
be mistaken for released features. Implementation changes must introduce useful
build and validation instructions with the code they exercise.

## Design and validation expectations

- Keep shared server, protocol, runtime, and client behavior aligned with
  [briosa](https://github.com/spatialanalyzer/briosa). Propose cross-repository
  changes there before relying on them here.
- Treat managed feeds, offline installation, noninteractive deployment,
  accessibility, and useful diagnostics as core requirements.
- Separate package installation from SDK activation and execution readiness.
  Use fake filesystem, feed, policy, registration, and process boundaries for
  ordinary tests; no SA installation or license should be required.
- Obtain explicit permission for the current task before controlling a desktop
  SA process, activating its SDK, changing registration, or using a licensed test
  environment. Follow reviewed vendor maintenance procedures.
- Do not include secrets, private hostnames, job data, license material, vendor
  binaries, or copied vendor documentation. Write examples with clearly fictitious
  data and use your own words.

Read [SECURITY.md](SECURITY.md) before reporting a suspected vulnerability.

## Contribution terms

Organization-wide contribution terms and unresolved policy questions live in
[governance/CONTRIBUTING.md](https://github.com/spatialanalyzer/governance/blob/main/CONTRIBUTING.md).
Contributions are made under the repository's [Apache-2.0 license](LICENSE).
This repository does not add a separate CLA or mandatory DCO requirement; follow
the published organization policy as it evolves.
