# 10. No game client files in the repository

- Status: Accepted
- Date: 2026-09-22

## Context

The game client is someone else's work. Its executables, assets and data files
are not ours to redistribute, and the server will eventually need some of
their contents, such as item and map data.

## Decision

No file from the game client is ever committed: no executables, assets or data
files, in any form, and nothing derived from them, including the packet layouts
and reference sessions the protocol code is checked against. Code that needs
client data will read it from a directory supplied by whoever runs the server
and mounted into the container at runtime. Tests that need such data will skip
when it is absent.

## Alternatives considered

- **Committing converted data** extracted from the client. This is still the
  client's content, only in a different format.
- **Downloading the client during the build.** Every build would then depend
  on a third party's files and their terms.

## Consequences

- The repository can be shared without redistributing anything from the
  client.
- Running the game server against a client requires your own copy of it.
- CI checks only what can be checked without client files. Anything that
  needs them is verified locally.
