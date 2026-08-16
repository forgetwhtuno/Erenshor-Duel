from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
control = (ROOT / "src" / "DuelControlApi.cs").read_text(encoding="utf-8")
aura = (ROOT / "src" / "DuelSuiteAuraProvider.cs").read_text(encoding="utf-8")
assert 'return DuelController.Active ? "Duel active" : "Idle | " + count + " eligible local candidate(s)";' in control
assert 'HasDedicatedPanel { get { return false; } }' in control
assert '"challenge,stop"' in aura
assert 'DuelControlApi.TryChallenge(argument)' in aura
assert 'DuelControlApi.TryStop()' in aura
assert 'eligibleCount' in aura and 'eligibleNames' not in aura
print("verify_suite_control_surface: PASS")
