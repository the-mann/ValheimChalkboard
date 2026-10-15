# Task list

- [ ] Allow a client with Chalkboard installed to join a server without Chalkboard. Detect the missing server capability and disable Chalkboard features for that session instead of failing the connection. Leave ordinary vanilla signs usable. Re-enable features when joining a supported server, and test both session transitions.

## Release approval

The repository owner authorizes committing/pushing changes or merging branches into `main`, and submitting new versions through the Thunderstore website once the work is complete and well tested. Validate the changed behavior with managed multiplayer tests, real headless engine/socket tests, compatibility checks, and GitHub Actions before publishing. Record actual evidence and distinguish automated coverage from live client checks.
