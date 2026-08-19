using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ErenshorDuel
{
    // Minimal standalone Sim Actions surface, installed ONLY while Follow's own Sim Actions system is
    // absent or unhealthy (see DuelFollowCompatibility). Follow owns the full Sim Actions experience
    // whenever it is healthy; this class observes the same native click hooks every frame but never
    // opens or keeps open any UI while DuelFollowCompatibility.IsFollowSimActionsHealthy() is true, so
    // there is exactly one Sim Actions interaction regardless of load order or hot load/unload.
    //
    // This is deliberately NOT a general Sim Actions menu: it exposes only Practice Duel (player-vs-
    // Sim) and Arrange Sim Duel (spectator). It never adds Follow/Lead/Expedition actions. Both paths
    // call the exact same DuelController entry points (Start/StartSpectator) and the exact same
    // eligibility decision (DuelController.EvaluateEligibility / DuelEligibilityPolicy) that the
    // /eduel command already uses -- no combat logic and no eligibility rule is duplicated here.
    internal static class DuelSimActionsFallback
    {
        private const int CanvasSortOrder = 536;
        private const float PanelWidth = 260f;
        private const float HeaderHeight = 30f;
        private const float ContentBottomMargin = 6f;
        private const float MinimumPanelHeight = 64f;

        private static readonly Color PanelFill = new Color32(4, 23, 32, 232);
        private static readonly Color HeaderFill = new Color32(6, 33, 43, 245);
        private static readonly Color ButtonFill = new Color32(9, 43, 56, 245);
        private static readonly Color ButtonHover = new Color32(31, 97, 122, 250);
        private static readonly Color ButtonPressed = new Color32(8, 171, 219, 255);
        private static readonly Color CyanAccent = new Color32(8, 171, 219, 245);
        private static readonly Color TitleCyan = new Color32(143, 224, 255, 255);
        private static readonly Color HintCyan = new Color32(143, 199, 224, 255);
        private static readonly Color WarnAmber = new Color32(255, 211, 132, 255);

        private static DuelFallbackMode _mode = DuelFallbackMode.Closed;
        private static SimPlayer _firstSim, _secondSim;
        private static string _firstName, _secondName;
        private static DuelEligibilityDecision _firstEligibility = DuelEligibilityDecision.NotSimPlayer;
        private static DuelEligibilityDecision _secondEligibility = DuelEligibilityDecision.NotSimPlayer;
        private static string _note = string.Empty;
        private static bool _noteWarning;
        private static int _lastSignature = int.MinValue;

        private static bool _nativeLeftClickActive;
        private static SimPlayer _nativeLeftClickTarget;

        private static GameObject _root, _panelObject;
        private static RectTransform _panel, _content;
        private static TextMeshProUGUI _titleText;

        internal static bool IsOpen { get { return _mode != DuelFallbackMode.Closed; } }

        // --- lifecycle -----------------------------------------------------------------------------

        internal static void Tick()
        {
            if (_mode == DuelFallbackMode.Closed) return;
            if (DuelFollowCompatibility.IsFollowSimActionsHealthy())
            {
                // Follow became healthy while our fallback was open (hot load, or its hooks recovered).
                StandDown();
                return;
            }
            if (!ErenshorDuelPlugin.DuelUiReady())
            {
                Close(DuelSimActionsFallbackPolicy.OnGameplayNotReady());
                return;
            }
            if (EventSystem.current == null)
            {
                Close(DuelSimActionsFallbackPolicy.OnGameplayNotReady());
                return;
            }

            _firstEligibility = EvaluateCandidate(_firstSim);
            if (DuelEligibilityPolicy.IsHardInvalid(_firstEligibility))
            {
                SetNote((string.IsNullOrEmpty(_firstName) ? "That Sim" : _firstName) + " is no longer available: " +
                    DuelEligibilityPolicy.DescribeForUi(_firstEligibility), true);
                Close(DuelSimActionsFallbackPolicy.OnFirstSimInvalid());
                return;
            }

            if (_mode == DuelFallbackMode.Confirm)
            {
                _secondEligibility = EvaluateCandidate(_secondSim);
                if (DuelEligibilityPolicy.IsHardInvalid(_secondEligibility))
                {
                    string lostName = string.IsNullOrEmpty(_secondName) ? "That Sim" : _secondName;
                    _secondSim = null;
                    _secondName = null;
                    _mode = DuelSimActionsFallbackPolicy.OnSecondSimInvalid(_mode);
                    SetNote(lostName + " is no longer available: " + DuelEligibilityPolicy.DescribeForUi(_secondEligibility), true);
                    RebuildIfChanged();
                    return;
                }
            }

            RebuildIfChanged();
            ClampPanelToScreen();
        }

        internal static void Shutdown()
        {
            Close(DuelFallbackMode.Closed);
            if (_root != null) { try { UnityEngine.Object.DestroyImmediate(_root); } catch { } }
            _root = _panelObject = null;
            _panel = _content = null;
            _titleText = null;
            _lastSignature = int.MinValue;
        }

        // --- native click observation (observation-only; never consumes or blocks the native click) -

        internal static void BeginNativeLeftClick()
        {
            _nativeLeftClickTarget = null;
            if (DuelFollowCompatibility.IsFollowSimActionsHealthy()) { StandDown(); return; }
            _nativeLeftClickActive = !PointerIsOverUi();
            // A miss-click on empty ground while hunting for a spectator opponent must not cancel the
            // arrangement; every other open mode closes on an outside click, matching Follow's own
            // Sim Actions convention.
            if (_nativeLeftClickActive && _mode != DuelFallbackMode.Closed &&
                !DuelSimActionsFallbackPolicy.IsChoosingOpponentClick(_mode))
                Close(DuelSimActionsFallbackPolicy.AfterCancel(DuelFallbackMode.Closed));
        }

        internal static void ObserveNativeTarget(Character character)
        {
            if (!_nativeLeftClickActive || character == null) return;
            SimPlayer sim = null;
            try { sim = character.GetComponent<SimPlayer>(); } catch { }
            _nativeLeftClickTarget = sim;
        }

        internal static void CompleteNativeLeftClick()
        {
            if (!_nativeLeftClickActive) return;
            SimPlayer clicked = _nativeLeftClickTarget;
            _nativeLeftClickActive = false;
            _nativeLeftClickTarget = null;
            if (clicked == null) return;
            if (DuelFollowCompatibility.IsFollowSimActionsHealthy()) { StandDown(); return; }
            HandleWorldClick(clicked);
        }

        private static bool PointerIsOverUi()
        {
            try { return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(); }
            catch { return false; }
        }

        // --- click routing ---------------------------------------------------------------------------

        private static void HandleWorldClick(SimPlayer clicked)
        {
            if (DuelSimActionsFallbackPolicy.IsChoosingOpponentClick(_mode))
            {
                TrySelectSecond(clicked);
                return;
            }
            OpenSimMenu(clicked);
        }

        private static void OpenSimMenu(SimPlayer candidate)
        {
            if (candidate == null) return;
            _firstSim = candidate;
            _firstName = ReadSimName(candidate);
            _firstEligibility = EvaluateCandidate(candidate);
            _secondSim = null;
            _secondName = null;
            _mode = DuelFallbackMode.SimMenu;
            SetNote(string.Empty, false);
            if (!EnsureBuilt()) return;
            RebuildContent();
            PlaceNearPointer();
            _panelObject.SetActive(true);
        }

        private static void TrySelectSecond(SimPlayer candidate)
        {
            if (candidate == null) return;
            // ReferenceEquals, not Unity's overloaded ==: a destroyed object still must not silently
            // read as "different" and let the same underlying Sim be picked as both sides again.
            bool sameSim = ReferenceEquals(candidate, _firstSim);
            DuelEligibilityDecision decision = sameSim ? DuelEligibilityDecision.NotSimPlayer : EvaluateCandidate(candidate);
            bool eligible = !sameSim && decision == DuelEligibilityDecision.Eligible;

            DuelFallbackMode next = DuelSimActionsFallbackPolicy.AfterOpponentSelected(_mode, sameSim, eligible);
            if (next == DuelFallbackMode.ChoosingOpponent)
            {
                SetNote(sameSim
                    ? "Choose a different Sim than " + _firstName + "."
                    : DuelEligibilityPolicy.DescribeForUi(decision), true);
                RebuildContent();
                return;
            }

            _secondSim = candidate;
            _secondName = ReadSimName(candidate);
            _secondEligibility = decision;
            _mode = next;
            SetNote(string.Empty, false);
            RebuildContent();
        }

        // --- button actions --------------------------------------------------------------------------

        private static void ChallengePlayerVsSim()
        {
            if (_firstSim == null || _firstEligibility != DuelEligibilityDecision.Eligible) return;
            // Same entry point /eduel <SimName> uses; DuelController re-validates and reports any
            // rejection itself, so no eligibility check is duplicated here beyond gating the button.
            DuelController.Start(_firstSim, DuelRequestOrigin.ExplicitPlayer);
            Close(DuelFallbackMode.Closed);
        }

        private static void ArrangeSpectator()
        {
            _mode = DuelSimActionsFallbackPolicy.AfterArrange(_mode);
            if (_mode != DuelFallbackMode.ChoosingOpponent) return;
            SetNote("Choose opponent for " + _firstName + "...", false);
            RebuildContent();
        }

        private static void StartArrangedSpectator()
        {
            if (_firstSim == null || _secondSim == null) return;
            // Same StartSpectator entry point /eduel <Sim A> vs <Sim B> uses; DuelController performs
            // the full authoritative eligibility/health/distance/cooldown re-check and reports any
            // rejection itself.
            DuelController.StartSpectator(_firstSim, _secondSim, DuelRequestOrigin.ExplicitPlayer);
            Close(DuelFallbackMode.Closed);
        }

        private static void CancelArrangement()
        {
            _secondSim = null;
            _secondName = null;
            _mode = DuelSimActionsFallbackPolicy.AfterCancel(_mode);
            SetNote(string.Empty, false);
            if (_mode == DuelFallbackMode.Closed) { Close(DuelFallbackMode.Closed); return; }
            RebuildContent();
        }

        // --- helpers ---------------------------------------------------------------------------------

        private static void StandDown()
        {
            Close(DuelFallbackMode.Closed);
        }

        private static void Close(DuelFallbackMode next)
        {
            bool wasOpen = _mode != DuelFallbackMode.Closed;
            _mode = next;
            if (next == DuelFallbackMode.Closed)
            {
                _firstSim = null; _secondSim = null;
                _firstName = null; _secondName = null;
                _note = string.Empty; _noteWarning = false;
                if (_panelObject != null) _panelObject.SetActive(false);
                _lastSignature = int.MinValue;
            }
            else if (wasOpen)
            {
                RebuildContent();
            }
        }

        private static DuelEligibilityDecision EvaluateCandidate(SimPlayer candidate)
        {
            if (candidate == null) return DuelEligibilityDecision.NotSimPlayer;
            Character localPlayer = null;
            try { localPlayer = GameData.PlayerControl == null ? null : GameData.PlayerControl.Myself; } catch { }
            Character simCharacter;
            NPC simNpc;
            bool partySim;
            return DuelController.EvaluateEligibility(candidate, localPlayer, out simCharacter, out simNpc, out partySim);
        }

        private static string ReadSimName(SimPlayer sim)
        {
            try
            {
                if (sim == null) return null;
                if (sim.MyStats != null && !string.IsNullOrWhiteSpace(sim.MyStats.MyName)) return sim.MyStats.MyName;
                return sim.gameObject == null ? null : sim.gameObject.name;
            }
            catch { return null; }
        }

        private static void SetNote(string value, bool warning)
        {
            _note = value ?? string.Empty;
            _noteWarning = warning;
        }

        // --- retained uGUI -----------------------------------------------------------------------------

        private static bool EnsureBuilt()
        {
            if (_root != null && _panel != null) return true;
            try
            {
                _root = new GameObject("ErenshorDuel.SimActionsFallbackUI");
                UnityEngine.Object.DontDestroyOnLoad(_root);
                Canvas canvas = _root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.overrideSorting = true;
                canvas.sortingOrder = CanvasSortOrder;
                CanvasScaler scaler = _root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = 1f;
                _root.AddComponent<GraphicRaycaster>();

                _panelObject = MakePanel("SimActions", _root.transform, CyanAccent);
                _panel = _panelObject.GetComponent<RectTransform>();
                BaseRect(_panel, PanelWidth, HeaderHeight + MinimumPanelHeight);

                // "Inner" is the PanelFill body. It must STRETCH to the full, dynamically-grown
                // _panel bounds (with a 1px inset so the CyanAccent panel color shows through as a
                // thin border) rather than use a fixed size: _panel.sizeDelta grows every
                // RebuildContent() call to fit the current content, but a fixed-size child does not
                // track that growth, which is exactly what left a large unfilled CyanAccent gap
                // above a header/body band pinned to the panel's bottom.
                RectTransform inner = new GameObject("Inner", typeof(RectTransform)).GetComponent<RectTransform>();
                inner.SetParent(_panel, false);
                inner.anchorMin = Vector2.zero;
                inner.anchorMax = Vector2.one;
                inner.pivot = new Vector2(0.5f, 0.5f);
                inner.offsetMin = new Vector2(1f, 1f);
                inner.offsetMax = new Vector2(-1f, -1f);
                inner.gameObject.AddComponent<Image>().color = PanelFill;

                // Header must anchor to the TOP of "inner" (which now always equals the panel's
                // actual top), not the bottom - otherwise it renders far below the visible top edge
                // whenever the panel is taller than the header's own height.
                RectTransform header = new GameObject("Header", typeof(RectTransform)).GetComponent<RectTransform>();
                header.SetParent(inner, false);
                header.anchorMin = new Vector2(0f, 1f);
                header.anchorMax = new Vector2(1f, 1f);
                header.pivot = new Vector2(0.5f, 1f);
                header.anchoredPosition = Vector2.zero;
                header.sizeDelta = new Vector2(0f, HeaderHeight);
                header.gameObject.AddComponent<Image>().color = HeaderFill;
                _titleText = AddText(header, "SIM ACTIONS", 12, TextAlignmentOptions.MidlineLeft, TitleCyan, false);
                SetOffsets(_titleText.rectTransform, 8f, 0f, -30f, 0f);
                RectTransform close = MakeRect("Close", header, 24f, 22f, PanelWidth - 30f, HeaderHeight * 0.5f - 11f);
                AddButton(close, "X", delegate { Close(DuelFallbackMode.Closed); }, false);

                GameObject contentObject = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
                _content = contentObject.GetComponent<RectTransform>();
                _content.SetParent(inner, false);
                _content.anchorMin = new Vector2(0f, 1f);
                _content.anchorMax = new Vector2(1f, 1f);
                _content.pivot = new Vector2(0.5f, 1f);
                _content.anchoredPosition = new Vector2(0f, -HeaderHeight);
                _content.sizeDelta = Vector2.zero;
                VerticalLayoutGroup layout = contentObject.GetComponent<VerticalLayoutGroup>();
                layout.padding = new RectOffset(6, 6, 6, 6);
                layout.spacing = 4f;
                layout.childControlHeight = true;
                layout.childControlWidth = true;
                layout.childForceExpandHeight = false;
                layout.childForceExpandWidth = true;
                ContentSizeFitter fitter = contentObject.GetComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                _panelObject.SetActive(false);
                return true;
            }
            catch
            {
                Shutdown();
                return false;
            }
        }

        private static void RebuildIfChanged()
        {
            int signature = ComputeSignature();
            if (signature == _lastSignature) return;
            _lastSignature = signature;
            RebuildContent();
        }

        private static int ComputeSignature()
        {
            unchecked
            {
                int h = (int)_mode * 31;
                h = h * 31 + (int)_firstEligibility;
                h = h * 31 + (int)_secondEligibility;
                h = h * 31 + (_note == null ? 0 : _note.GetHashCode());
                return h;
            }
        }

        private static void RebuildContent()
        {
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(_content.GetChild(i).gameObject);

            switch (_mode)
            {
                case DuelFallbackMode.SimMenu: BuildSimMenu(); break;
                case DuelFallbackMode.ChoosingOpponent: BuildChoosingOpponent(); break;
                case DuelFallbackMode.Confirm: BuildConfirm(); break;
                default: break;
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            // "Inner" and "header" both track _panel.sizeDelta through stretch anchors (see
            // EnsureBuilt), so growing _panel here is the ONLY thing that has to happen for the
            // rest of the hierarchy to stay internally consistent - no separate child resize step.
            float height = HeaderHeight + LayoutUtility.GetPreferredHeight(_content) + ContentBottomMargin;
            _panel.sizeDelta = new Vector2(PanelWidth, Mathf.Max(MinimumPanelHeight, height));
        }

        private static void BuildSimMenu()
        {
            AddRow(string.IsNullOrEmpty(_firstName) ? "Selected Sim" : _firstName, HintCyan, 20f);
            bool eligible = _firstEligibility == DuelEligibilityDecision.Eligible;
            AddActionButton("Practice Duel", eligible, ChallengePlayerVsSim);
            AddActionButton("Arrange Sim Duel", eligible, ArrangeSpectator);
            AddNoteRow(eligible ? _note : DuelEligibilityPolicy.DescribeForUi(_firstEligibility), !eligible);
        }

        private static void BuildChoosingOpponent()
        {
            AddRow("Choose opponent for " + (_firstName ?? "this Sim"), HintCyan, 20f);
            AddNoteRow(string.IsNullOrEmpty(_note) ? "Click another eligible local Sim." : _note, _noteWarning);
            AddActionButton("Cancel", true, CancelArrangement);
        }

        private static void BuildConfirm()
        {
            AddRow((_firstName ?? "?") + " vs " + (_secondName ?? "?"), HintCyan, 20f);
            bool ready = _firstEligibility == DuelEligibilityDecision.Eligible &&
                         _secondEligibility == DuelEligibilityDecision.Eligible;
            AddActionButton("Start", ready, StartArrangedSpectator);
            AddActionButton("Cancel", true, CancelArrangement);
            if (!ready)
                AddNoteRow(DuelEligibilityPolicy.DescribeForUi(
                    _firstEligibility != DuelEligibilityDecision.Eligible ? _firstEligibility : _secondEligibility), true);
        }

        private static void AddRow(string text, Color color, float height)
        {
            RectTransform row = MakeContentRow(height);
            AddText(row, text, 12, TextAlignmentOptions.MidlineLeft, color, false);
        }

        private static void AddNoteRow(string text, bool warning)
        {
            if (string.IsNullOrEmpty(text)) return;
            RectTransform row = MakeContentRow(30f);
            AddText(row, text, 10, TextAlignmentOptions.TopLeft, warning ? WarnAmber : HintCyan, true);
        }

        private static void AddActionButton(string label, bool enabled, UnityEngine.Events.UnityAction action)
        {
            RectTransform row = MakeContentRow(28f);
            Button button = AddButton(row, label, action, false);
            button.interactable = enabled;
        }

        private static RectTransform MakeContentRow(float height)
        {
            GameObject go = new GameObject("Row", typeof(RectTransform), typeof(LayoutElement));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(_content, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, height);
            LayoutElement e = go.GetComponent<LayoutElement>();
            e.preferredHeight = height;
            e.minHeight = height;
            e.flexibleHeight = 0f;
            return rt;
        }

        private static void PlaceNearPointer()
        {
            if (_panel == null) return;
            Vector3 mouse = Input.mousePosition;
            _panel.anchoredPosition = new Vector2(mouse.x + 6f, mouse.y - 8f);
            ClampPanelToScreen();
        }

        private static void ClampPanelToScreen()
        {
            if (_panel == null) return;
            Vector2 size = _panel.sizeDelta;
            Vector2 p = _panel.anchoredPosition;
            p.x = Mathf.Clamp(p.x, 4f, Mathf.Max(4f, Screen.width - size.x - 4f));
            p.y = Mathf.Clamp(p.y, size.y + 4f, Mathf.Max(size.y + 4f, Screen.height - 4f));
            _panel.anchoredPosition = p;
        }

        private static GameObject MakePanel(string name, Transform parent, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            CanvasGroup group = go.GetComponent<CanvasGroup>();
            group.interactable = true;
            group.blocksRaycasts = true;
            return go;
        }

        private static void BaseRect(RectTransform rt, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            rt.sizeDelta = new Vector2(width, height);
        }

        private static RectTransform MakeRect(string name, Transform parent, float width, float height, float x, float y)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            BaseRect(rt, width, height);
            rt.anchoredPosition = new Vector2(x, y);
            return rt;
        }

        private static Button AddButton(RectTransform rt, string label, UnityEngine.Events.UnityAction action, bool caution)
        {
            Image image = rt.gameObject.GetComponent<Image>();
            if (image == null) image = rt.gameObject.AddComponent<Image>();
            Button button = rt.gameObject.GetComponent<Button>();
            if (button == null) button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            ColorBlock colors = button.colors;
            colors.normalColor = ButtonFill;
            colors.highlightedColor = ButtonHover;
            colors.pressedColor = ButtonPressed;
            colors.selectedColor = ButtonHover;
            colors.disabledColor = new Color32(8, 31, 40, 145);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            image.color = Color.white;
            AddText(rt, label, 11, TextAlignmentOptions.Center, Color.white, false);
            return button;
        }

        private static TextMeshProUGUI AddText(RectTransform parent, string text, int size,
            TextAlignmentOptions alignment, Color color, bool wrap)
        {
            GameObject go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(4f, 1f);
            rt.offsetMax = new Vector2(-4f, -1f);
            TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.alignment = alignment;
            label.color = color;
            label.raycastTarget = false;
            label.enableWordWrapping = wrap;
            label.overflowMode = wrap ? TextOverflowModes.Truncate : TextOverflowModes.Ellipsis;
            return label;
        }

        private static void SetOffsets(RectTransform rt, float left, float bottom, float right, float top)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(right, top);
        }
    }

    [HarmonyPatch(typeof(PlayerControl), "LeftClick")]
    internal static class DuelSimActionsFallbackLeftClickPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            try { DuelSimActionsFallback.BeginNativeLeftClick(); } catch { }
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
            try { DuelSimActionsFallback.CompleteNativeLeftClick(); } catch { }
        }
    }

    [HarmonyPatch(typeof(Character), "TargetMe")]
    internal static class DuelSimActionsFallbackTargetPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Character __instance)
        {
            try { DuelSimActionsFallback.ObserveNativeTarget(__instance); } catch { }
        }
    }
}
