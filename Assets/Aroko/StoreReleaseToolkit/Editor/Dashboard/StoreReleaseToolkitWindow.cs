using System;
using Aroko.StoreRelease.Editor.Configuration;
using UnityEditor;
using UnityEngine;

namespace Aroko.StoreRelease.Editor.Dashboard
{
    public sealed class StoreReleaseToolkitWindow : EditorWindow
    {
        private const float SidebarWidth = 184f;
        private const float NarrowLayoutWidth = 735f;

        private static readonly string[] PageTitles =
        {
            "Setup",
            "Build",
            "Diagnostics",
            "API"
        };

        private static readonly string[] PageDescriptions =
        {
            "Configure store SDKs, identifiers, and packaging.",
            "Validate and create a store-specific Windows x64 build.",
            "Find release blockers and verify the active project configuration.",
            "Browse recommended APIs, examples, and live codebase usage."
        };

        private static readonly string[] NavigationDescriptions =
        {
            "SDKs, IDs & profiles",
            "Build workflow",
            "Readiness checks",
            "Examples & usage"
        };

        private static readonly string[] ContextHelpDescriptions =
        {
            "Profiles, public IDs, SDK detection, and packaging settings.",
            "The local validation and Windows x64 build sequence.",
            "Diagnostic causes, exact fixes, and verification steps.",
            "Recommended runtime and editor APIs with project usage counts."
        };

        private IStoreReleaseDashboardPage[] pages;
        private StoreReleaseDashboardContext context;
        [SerializeField] private Vector2[] scrollPositions = new Vector2[4];
        [SerializeField] private int selectedPage;

        private GUIStyle productTitleStyle;
        private GUIStyle productSubtitleStyle;
        private GUIStyle navigationTitleStyle;
        private GUIStyle navigationDescriptionStyle;
        private GUIStyle navigationStepStyle;
        private GUIStyle footerStyle;
        private bool styleSkin;

        [MenuItem("Window/Store Release Toolkit")]
        public static void ShowWindow()
        {
            StoreReleaseToolkitWindow window =
                GetWindow<StoreReleaseToolkitWindow>("Store Release Toolkit");
            window.minSize = new Vector2(700f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            minSize = new Vector2(700f, 560f);
            context = new StoreReleaseDashboardContext();
            context.RequestNavigation = SelectPage;
            context.RequestRepaint = Repaint;
            pages = new IStoreReleaseDashboardPage[]
            {
                new StoreReleaseSetupPage(),
                new StoreReleaseBuildPage(),
                new StoreReleaseDiagnosticsPage(),
                new StoreReleaseApiPage()
            };
            if (scrollPositions == null || scrollPositions.Length != pages.Length)
            {
                scrollPositions = new Vector2[pages.Length];
            }

            selectedPage = Mathf.Clamp(selectedPage, 0, pages.Length - 1);
            if (RequiresSetup(selectedPage) && !context.IsSetupReady)
            {
                selectedPage = 0;
            }
            pages[selectedPage].OnActivated(context);
        }

        private void OnGUI()
        {
            if (context == null || pages == null)
            {
                OnEnable();
            }

            HandleKeyboardHelp();
            DrawHeader();

            if (position.width < NarrowLayoutWidth)
            {
                DrawCompactNavigation();
                DrawPageContent();
            }
            else
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawSidebar();
                    GUILayout.Space(8f);
                    using (new EditorGUILayout.VerticalScope())
                    {
                        DrawPageContent();
                    }
                }
            }

            DrawFooter();
        }

        private void OnDisable()
        {
            if (pages == null)
            {
                return;
            }

            foreach (IStoreReleaseDashboardPage page in pages)
            {
                (page as IDisposable)?.Dispose();
            }
        }

        private void DrawHeader()
        {
            EnsureStyles();
            using (new EditorGUILayout.VerticalScope(DashboardGui.CardStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUILayout.VerticalScope())
                    {
                        EditorGUILayout.LabelField(
                            "STORE RELEASE TOOLKIT",
                            productTitleStyle);
                        EditorGUILayout.LabelField(
                            "Configure, validate, and create clean store-specific builds.",
                            productSubtitleStyle);
                    }

                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(
                            DashboardGui.Content(
                                "Setup Guide",
                                "Open the interactive offline setup guide in your " +
                                "default browser."),
                            GUILayout.Width(92f),
                            GUILayout.Height(24f)))
                    {
                        StoreReleaseDocumentation.Open(
                            StoreReleaseDocumentation.Page.SetupGuide);
                    }

                    if (GUILayout.Button(
                            DashboardGui.Content(
                                "API Reference",
                                "Open the searchable offline editor API reference."),
                            GUILayout.Width(100f),
                            GUILayout.Height(24f)))
                    {
                        StoreReleaseDocumentation.Open(
                            StoreReleaseDocumentation.Page.ApiReference);
                    }

                    if (GUILayout.Button(
                            DashboardGui.Content(
                                "Help Center",
                                "Open offline troubleshooting, build runbooks, " +
                                "CI helpers, and glossary."),
                            GUILayout.Width(90f),
                            GUILayout.Height(24f)))
                    {
                        StoreReleaseDocumentation.Open(
                            StoreReleaseDocumentation.Page.HelpCenter);
                    }
                }

                GUILayout.Space(5f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    StoreBuildProfile profile = context.ActiveProfile;
                    EditorGUILayout.LabelField(
                        "Active release",
                        EditorStyles.miniLabel,
                        GUILayout.Width(80f));
                    DashboardGui.DrawBadge(
                        profile == null ? "NO PROFILE" : profile.DisplayName,
                        profile == null
                            ? DashboardGui.BadgeTone.Danger
                            : DashboardGui.BadgeTone.Neutral);

                    if (profile != null)
                    {
                        DashboardGui.DrawBadge(
                            profile.Store.ToString().ToUpperInvariant(),
                            DashboardGui.BadgeTone.Info);
                        DashboardGui.DrawBadge(
                            profile.Channel.ToString().ToUpperInvariant(),
                            ChannelTone(profile.Channel));
                    }

                    DashboardGui.DrawBadge(
                        "VERSION " +
                        (string.IsNullOrWhiteSpace(context.Version)
                            ? "NOT SET"
                            : context.Version),
                        string.IsNullOrWhiteSpace(context.Version)
                            ? DashboardGui.BadgeTone.Warning
                            : DashboardGui.BadgeTone.Neutral);
                    GUILayout.FlexibleSpace();

                    if (EditorApplication.isCompiling)
                    {
                        DashboardGui.DrawBadge(
                            "COMPILING",
                            DashboardGui.BadgeTone.Warning);
                    }
                    else if (BuildPipeline.isBuildingPlayer)
                    {
                        DashboardGui.DrawBadge(
                            "BUILDING",
                            DashboardGui.BadgeTone.Warning);
                    }
                }
            }
        }

        private void DrawSidebar()
        {
            EnsureStyles();
            using (new EditorGUILayout.VerticalScope(
                       EditorStyles.helpBox,
                       GUILayout.Width(SidebarWidth),
                       GUILayout.ExpandHeight(true)))
            {
                GUILayout.Space(6f);
                EditorGUILayout.LabelField(
                    "RELEASE WORKFLOW",
                    EditorStyles.miniLabel);
                GUILayout.Space(3f);
                for (int index = 0; index < PageTitles.Length; index++)
                {
                    DrawNavigationItem(index);
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.HelpBox(
                    "The toolkit creates local build folders only. Store delivery is handled separately.",
                    MessageType.Info);
            }
        }

        private void DrawNavigationItem(int index)
        {
            bool locked = RequiresSetup(index) && !context.IsSetupReady;
            bool selected = index == selectedPage;
            Rect itemRect = GUILayoutUtility.GetRect(
                SidebarWidth - 14f,
                58f,
                GUILayout.ExpandWidth(true));
            bool hovered = itemRect.Contains(Event.current.mousePosition);
            Color background = selected
                ? SelectedNavigationColor()
                : hovered
                    ? HoverNavigationColor()
                    : Color.clear;
            if (background.a > 0f)
            {
                EditorGUI.DrawRect(itemRect, background);
            }

            if (selected)
            {
                EditorGUI.DrawRect(
                    new Rect(itemRect.x, itemRect.y, 3f, itemRect.height),
                    AccentColor());
            }

            Rect stepRect = new Rect(
                itemRect.x + 10f,
                itemRect.y + 12f,
                26f,
                26f);
            EditorGUI.DrawRect(
                stepRect,
                selected ? AccentColor() : StepColor());
            using (new EditorGUI.DisabledScope(locked))
            {
                GUI.Label(
                    stepRect,
                    (index + 1).ToString(),
                    navigationStepStyle);

            Rect titleRect = new Rect(
                itemRect.x + 45f,
                itemRect.y + 8f,
                itemRect.width - 51f,
                20f);
                GUI.Label(titleRect, PageTitles[index], navigationTitleStyle);
            Rect descriptionRect = new Rect(
                titleRect.x,
                itemRect.y + 29f,
                titleRect.width,
                22f);
                GUI.Label(
                    descriptionRect,
                    locked ? "Complete Setup first" : NavigationDescriptions[index],
                    navigationDescriptionStyle);

                if (!locked)
                {
                    EditorGUIUtility.AddCursorRect(itemRect, MouseCursor.Link);
                }

                if (GUI.Button(
                        itemRect,
                        DashboardGui.Content(
                            string.Empty,
                            locked
                                ? "Complete Setup before opening " + PageTitles[index] + "."
                                : "Open " + PageTitles[index] + ": " + PageDescriptions[index]),
                        GUIStyle.none))
                {
                    SelectPage(index);
                }
            }
        }

        private void DrawCompactNavigation()
        {
            var tabs = new GUIContent[PageTitles.Length];
            for (int index = 0; index < PageTitles.Length; index++)
            {
                tabs[index] = DashboardGui.Content(
                    (index + 1) + "  " + PageTitles[index],
                    PageDescriptions[index]);
            }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                for (int index = 0; index < tabs.Length; index++)
                {
                    bool locked = RequiresSetup(index) && !context.IsSetupReady;
                    using (new EditorGUI.DisabledScope(locked))
                    {
                        bool chosen = GUILayout.Toggle(
                            selectedPage == index,
                            tabs[index],
                            EditorStyles.toolbarButton,
                            GUILayout.Height(23f),
                            GUILayout.ExpandWidth(true));
                        if (chosen && index != selectedPage)
                        {
                            SelectPage(index);
                        }
                    }
                }
            }

            GUILayout.Space(4f);
        }

        private void DrawPageContent()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                DashboardGui.DrawPageHeader(
                    "Step " + (selectedPage + 1) + " of " + pages.Length,
                    PageTitles[selectedPage],
                    PageDescriptions[selectedPage]);
                DrawContextHelp();
                scrollPositions[selectedPage] = EditorGUILayout.BeginScrollView(
                    scrollPositions[selectedPage]);
                pages[selectedPage].Draw(context);
                GUILayout.Space(16f);
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawFooter()
        {
            EnsureStyles();
            GUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope(footerStyle))
            {
                int previousPage = PreviousAccessiblePage(selectedPage);
                using (new EditorGUI.DisabledScope(previousPage < 0))
                {
                    if (GUILayout.Button(
                            DashboardGui.Content(
                                "Back",
                                "Return to the previous build-toolkit step."),
                            GUILayout.Width(72f),
                            GUILayout.Height(24f)))
                    {
                        SelectPage(previousPage);
                    }
                }

                GUILayout.Space(6f);
                EditorGUILayout.LabelField(
                    "Step " + (selectedPage + 1) + " of " + pages.Length,
                    EditorStyles.miniLabel,
                    GUILayout.Width(72f));
                GUILayout.FlexibleSpace();

                if (GUILayout.Button(
                        DashboardGui.Content(
                            "Build Checklist",
                            "Open the persistent offline build checklist."),
                        EditorStyles.linkLabel,
                        GUILayout.Width(96f)))
                {
                    StoreReleaseDocumentation.Open(
                        StoreReleaseDocumentation.Page.SetupGuide,
                        "build-checklist");
                }

                GUILayout.Space(8f);
                bool nextLocked = selectedPage == pages.Length - 1 ||
                                  (selectedPage == 0 && !context.IsSetupReady);
                using (new EditorGUI.DisabledScope(nextLocked))
                {
                    string nextTitle = selectedPage < pages.Length - 1
                        ? "Next: " + PageTitles[selectedPage + 1]
                        : "Complete";
                    if (GUILayout.Button(
                            DashboardGui.Content(
                                nextTitle,
                                "Continue to the next build-toolkit step."),
                            GUILayout.Width(142f),
                            GUILayout.Height(24f)))
                    {
                        SelectPage(selectedPage + 1);
                    }
                }
            }
        }

        private void SelectPage(int pageIndex)
        {
            if (pages == null || pages.Length == 0)
            {
                return;
            }

            int nextPage = Mathf.Clamp(pageIndex, 0, pages.Length - 1);
            if (RequiresSetup(nextPage) && !context.IsSetupReady)
            {
                return;
            }
            if (nextPage == selectedPage)
            {
                return;
            }

            selectedPage = nextPage;
            pages[selectedPage].OnActivated(context);
            GUI.FocusControl(null);
            Repaint();
        }

        private int PreviousAccessiblePage(int pageIndex)
        {
            for (int candidate = pageIndex - 1; candidate >= 0; candidate--)
            {
                if (!RequiresSetup(candidate) || context.IsSetupReady)
                {
                    return candidate;
                }
            }

            return -1;
        }

        private static bool RequiresSetup(int pageIndex)
        {
            return pageIndex == 1 || pageIndex == 2;
        }

        private void DrawContextHelp()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    ContextHelpDescriptions[selectedPage],
                    EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(
                        DashboardGui.Content(
                            "Open step guide",
                            "Open focused offline help for this workflow step. " +
                            "Press F1 while this window is focused."),
                        EditorStyles.linkLabel,
                        GUILayout.Width(92f)))
                {
                    StoreReleaseDocumentation.OpenForWorkflowStep(selectedPage);
                }
            }

            GUILayout.Space(3f);
        }

        private void HandleKeyboardHelp()
        {
            Event current = Event.current;
            if (current == null ||
                current.type != EventType.KeyDown ||
                current.keyCode != KeyCode.F1)
            {
                return;
            }

            StoreReleaseDocumentation.OpenForWorkflowStep(selectedPage);
            current.Use();
        }

        private void EnsureStyles()
        {
            bool currentSkin = EditorGUIUtility.isProSkin;
            if (productTitleStyle != null && currentSkin == styleSkin)
            {
                return;
            }

            styleSkin = currentSkin;
            productTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 15,
                fixedHeight = 20f
            };
            productSubtitleStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                fontSize = 11,
                wordWrap = true
            };
            navigationTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                clipping = TextClipping.Clip
            };
            navigationDescriptionStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 9,
                clipping = TextClipping.Clip
            };
            navigationStepStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal =
                {
                    textColor = currentSkin
                        ? Color.white
                        : new Color(0.08f, 0.08f, 0.08f, 1f)
                }
            };
            footerStyle = new GUIStyle(EditorStyles.toolbar)
            {
                padding = new RectOffset(7, 7, 4, 4),
                fixedHeight = 32f
            };
        }

        private static DashboardGui.BadgeTone ChannelTone(
            StoreReleaseChannel channel)
        {
            switch (channel)
            {
                case StoreReleaseChannel.Live:
                    return DashboardGui.BadgeTone.Success;
                case StoreReleaseChannel.Stage:
                    return DashboardGui.BadgeTone.Warning;
                default:
                    return DashboardGui.BadgeTone.Info;
            }
        }

        private static Color AccentColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.25f, 0.58f, 0.92f, 1f)
                : new Color(0.12f, 0.46f, 0.82f, 1f);
        }

        private static Color SelectedNavigationColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.18f, 0.34f, 0.50f, 0.72f)
                : new Color(0.70f, 0.84f, 0.98f, 0.80f);
        }

        private static Color HoverNavigationColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.35f, 0.37f, 0.40f, 0.42f)
                : new Color(0.76f, 0.78f, 0.82f, 0.42f);
        }

        private static Color StepColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.31f, 0.33f, 0.36f, 1f)
                : new Color(0.76f, 0.78f, 0.82f, 1f);
        }

    }
}
