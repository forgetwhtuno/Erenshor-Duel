using System;
using System.Collections.Generic;

namespace ErenshorDuel
{
    public sealed class DuelSemanticEvent
    {
        public string Type { get; private set; }
        public string OpponentName { get; private set; }
        public string OpponentScope { get; private set; }
        public string Decision { get; private set; }
        public string Outcome { get; private set; }
        public string Winner { get; private set; }
        public string Yielded { get; private set; }
        public string ReasonToken { get; private set; }
        public string Reason { get; private set; }

        internal DuelSemanticEvent(string type, string opponentName, string opponentScope,
            string decision, string outcome, string winner, string yielded,
            string reasonToken, string reason)
        {
            Type = Clean(type);
            OpponentName = Clean(opponentName);
            OpponentScope = Clean(opponentScope);
            Decision = Clean(decision);
            Outcome = Clean(outcome);
            Winner = Clean(winner);
            Yielded = Clean(yielded);
            ReasonToken = Clean(reasonToken);
            Reason = Clean(reason);
        }

        // The existing Deep Sims generic observed-game-event bridge takes a description string.
        // Keep that fallback deterministic and fact-only while exposing the structured event above.
        public string ToObservedGameEventDescription()
        {
            List<string> fields = new List<string>();
            Add(fields, "type", Type);
            Add(fields, "opponent", OpponentName);
            Add(fields, "scope", OpponentScope);
            Add(fields, "decision", Decision);
            Add(fields, "outcome", Outcome);
            Add(fields, "winner", Winner);
            Add(fields, "yielded", Yielded);
            Add(fields, "reason_token", ReasonToken);
            Add(fields, "reason", Reason);
            return string.Join("; ", fields.ToArray());
        }

        private static void Add(List<string> fields, string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) fields.Add(key + "=" + value);
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string clean = value.Replace('\r', ' ').Replace('\n', ' ').Replace(';', ',').Replace('=', ':').Trim();
            return clean.Length <= 160 ? clean : clean.Substring(0, 160);
        }
    }

    // Small optional public surface for future consumers. Practice Duels remains standalone;
    // subscribers are isolated so one third-party exception cannot affect duel state.
    public static class PracticeDuelEvents
    {
        public const int ContractVersion = 2;
        public static event Action<DuelSemanticEvent> SemanticEvent;

        internal static void Publish(DuelSemanticEvent value)
        {
            Action<DuelSemanticEvent> handlers = SemanticEvent;
            if (handlers == null || value == null) return;
            foreach (Delegate raw in handlers.GetInvocationList())
            {
                try { ((Action<DuelSemanticEvent>)raw)(value); }
                catch { }
            }
        }
    }

    internal static class DuelEventFactory
    {
        private static string Scope(bool partySim) { return partySim ? "party" : "nearby"; }

        internal static DuelSemanticEvent Challenge(string opponent, bool partySim)
        {
            return New("duel_challenge", opponent, partySim, string.Empty,
                string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        }

        internal static DuelSemanticEvent Accepted(string opponent, bool partySim)
        {
            return New("duel_accepted", opponent, partySim, "accept",
                string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        }

        internal static DuelSemanticEvent Declined(string opponent, bool partySim, string decision)
        {
            return New("duel_declined", opponent, partySim, decision,
                string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        }

        internal static DuelSemanticEvent Started(string opponent, bool partySim)
        {
            return New("duel_started", opponent, partySim, string.Empty,
                string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        }

        internal static DuelSemanticEvent Completed(string opponent, bool partySim, string outcome, string winner, string yielded)
        {
            return New("duel_completed", opponent, partySim, string.Empty,
                outcome, winner, yielded, string.Empty, string.Empty);
        }

        internal static DuelSemanticEvent Cancelled(string opponent, bool partySim, string reasonToken, string reason)
        {
            return New("duel_cancelled", opponent, partySim, string.Empty,
                string.Empty, string.Empty, string.Empty, reasonToken, reason);
        }

        // Stable reason tokens let optional social consumers distinguish meaningful causes without
        // parsing chat prose.  The detailed reason remains diagnostic only; the token is authoritative.
        internal static string CancellationToken(string source, string reason)
        {
            string s = (source ?? string.Empty).Trim().ToLowerInvariant();
            string r = (reason ?? string.Empty).Trim().ToLowerInvariant();

            if (s.IndexOf("attackingplayer", StringComparison.Ordinal) >= 0 ||
                r.IndexOf("outside actor", StringComparison.Ordinal) >= 0 ||
                r.IndexOf("outside attacker", StringComparison.Ordinal) >= 0 ||
                r.IndexOf("outside hostile", StringComparison.Ordinal) >= 0 ||
                r.IndexOf("real combat", StringComparison.Ordinal) >= 0 ||
                r.IndexOf("party combat", StringComparison.Ordinal) >= 0)
                return "hostile_interruption";
            if (s.IndexOf("scene", StringComparison.Ordinal) >= 0 || s.IndexOf("zone", StringComparison.Ordinal) >= 0 ||
                r.IndexOf("zone", StringComparison.Ordinal) >= 0)
                return "zone_change";
            if (s.IndexOf("camp", StringComparison.Ordinal) >= 0 || r.IndexOf("camp mode", StringComparison.Ordinal) >= 0)
                return "camp";
            if (s.IndexOf("distance", StringComparison.Ordinal) >= 0 || r.IndexOf("too far", StringComparison.Ordinal) >= 0)
                return "distance";
            if (s.IndexOf("participant", StringComparison.Ordinal) >= 0 || r.IndexOf("no longer available", StringComparison.Ordinal) >= 0)
                return "participant_unavailable";
            if (s.IndexOf("exception", StringComparison.Ordinal) >= 0 || s.IndexOf("npcprocguard", StringComparison.Ordinal) >= 0 ||
                r.IndexOf("internal error", StringComparison.Ordinal) >= 0 || r.IndexOf("could not start safely", StringComparison.Ordinal) >= 0)
                return "internal_error";
            if (r.IndexOf("practice duel stopped", StringComparison.Ordinal) >= 0 || r.IndexOf("duel stopped", StringComparison.Ordinal) >= 0)
                return "manual_stop";
            return "other";
        }

        private static DuelSemanticEvent New(string type, string opponent, bool partySim,
            string decision, string outcome, string winner, string yielded, string reasonToken, string reason)
        {
            return new DuelSemanticEvent(type, opponent, Scope(partySim), decision,
                outcome, winner, yielded, reasonToken, reason);
        }

        internal static string RunSelfTests()
        {
            DuelSemanticEvent challenge = Challenge("Dancer", false);
            if (challenge.Type != "duel_challenge" || challenge.OpponentScope != "nearby")
                return "FAIL events: challenge shape";

            DuelSemanticEvent accepted = Accepted("Dancer", true);
            if (accepted.Type != "duel_accepted" || accepted.Decision != "accept" || accepted.OpponentScope != "party")
                return "FAIL events: accept shape";

            DuelSemanticEvent declined = Declined("Dancer", false, "decline_recent_duel");
            if (declined.Type != "duel_declined" || declined.Decision != "decline_recent_duel")
                return "FAIL events: decline shape";

            DuelSemanticEvent started = Started("Dancer", false);
            if (started.Type != "duel_started") return "FAIL events: start shape";

            DuelSemanticEvent completed = Completed("Dancer", false, "yield", "player", "opponent");
            if (completed.Type != "duel_completed" || completed.Winner != "player" || completed.Yielded != "opponent")
                return "FAIL events: completion shape";

            DuelSemanticEvent cancelled = Cancelled("Dancer", true, "distance", "Duelists moved too far apart.");
            if (cancelled.Type != "duel_cancelled" || cancelled.ReasonToken != "distance")
                return "FAIL events: cancel shape";

            if (CancellationToken("Tick.AttackingPlayer", "verified outside attacker entered party combat") != "hostile_interruption" ||
                CancellationToken("Tick.Camp", "camp mode is active") != "camp" ||
                CancellationToken("Stop.Fallback", "Practice duel stopped.") != "manual_stop")
                return "FAIL events: cancellation tokens";

            string description = completed.ToObservedGameEventDescription();
            if (description.IndexOf("type=duel_completed", StringComparison.Ordinal) < 0 ||
                description.IndexOf("outcome=yield", StringComparison.Ordinal) < 0 ||
                description.IndexOf("winner=player", StringComparison.Ordinal) < 0)
                return "FAIL events: generic fallback shape";

            string cancelDescription = cancelled.ToObservedGameEventDescription();
            if (cancelDescription.IndexOf("reason_token=distance", StringComparison.Ordinal) < 0)
                return "FAIL events: cancellation fallback token";

            return "PASS events";
        }
    }
}
