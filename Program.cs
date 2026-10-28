using System.Globalization;

// Pinned process-wide, before anything else runs: every numeric format string in this codebase
// (`{amount:F2}`, `{pct:F1}%`, etc., scattered across dozens of ledger/embed/response strings) uses
// the current thread's culture by default. Left unset, that's whatever locale the host OS happens to
// be configured with — on this machine, Czech (comma decimal separators: "7,0" instead of "7.0"),
// which is exactly what showed up in ledger text and would show up in every Discord response too.
// Setting the DEFAULT culture here means every thread the process creates afterward — including the
// thread-pool threads Task.Run spins up per interaction in Bot.cs — inherits invariant (period-
// decimal) formatting, without needing CultureInfo.InvariantCulture threaded through every call site.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var bot = new Bot();
await bot.RunAsync();
