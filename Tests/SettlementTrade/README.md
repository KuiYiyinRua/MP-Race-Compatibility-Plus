# Settlement trade regression

The test-only assembly is disabled unless `-settlementprobe` is present. The
controller starts isolated real-graphics host/client processes, compares frozen
world/map RNG baselines, and uses the non-host caravan's native settlement Trade
command, TradingWindow, close/reopen, and native Accept callback.

Build `Probe.csproj`, then run `Run.ps1 -RunRoot <new absolute directory>
-Candidate <absolute core DLL>`. Add `-Old` for revision-5 reproduction: three
distinct item classes fail as ambiguous, followed by a selected stock object
destroyed by regeneration. Candidate mode buys and sells six distinct physical
fixture items (two minified buildings, two different genepacks, two differently
titled books), plus an animal and a minified building from untouched native
stock, for sixteen transactions; both peers must assert the selected identity, ownership, unchanged
unrelated stock, native session removal, and zero regeneration during accept.
Each round reopens the settlement window. The candidate run continues for
120,000 shared Multiplayer ticks after the action matrix. Use `-AboutPath` to
test the exact candidate metadata; `-Trace` enables diagnostic tracing.

The fixtures are inserted only through a registered shared test command. They
are explicitly fixture goods, not claims of natural trader generation. The
old run additionally logs collisions already present in the untouched vanilla
settlement stock before insertion. No player save/config is read or modified.

`MetadataAudit.csproj` compares all types and method bodies outside the two
modified trade classes with the installed baseline. Unrelated pending config
changes must not be included in a candidate. Use the installed build's compiler
configuration; Debug/Release optimization differences invalidate an IL audit.

Reject any action assertion, command, join, or desync failure. The controller
keeps logs, hashes, process command lines, paired receipts, and terminal status.
Runtime ModsConfig formatting may change, but the active IDs/order must remain
identical and both peer configuration hashes must match. Test DLLs must never
be copied into release assemblies.
