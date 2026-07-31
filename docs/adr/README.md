# Architecture Decision Records

Short records of decisions that shaped this repo, kept so the reasoning survives the
people who were in the room.

One file per decision, numbered in the order taken. A record is written when a choice
has consequences someone would otherwise have to reverse-engineer from the code — or
when a known limitation is being *accepted* rather than fixed, which is the case a
codebase is least able to explain about itself.

Records are immutable once merged. A decision that changes gets a new record that
supersedes the old one, and the old one is marked as superseded rather than edited.

| # | Decision | Status |
|---|----------|--------|
| [0001](0001-transactional-outbox-for-command-publishing.md) | Transactional outbox for command publishing | Accepted |
| [0002](0002-accept-the-inbox-recheck-race.md) | Accept the inbox re-check race rather than lock | Accepted |
| [0003](0003-deterministic-event-ids-instead-of-a-worker-inbox.md) | Deterministic event ids instead of an inbox in the analysis service | Accepted |
