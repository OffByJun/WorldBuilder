using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldBuilder.Editor.PrefabBrush
{
    /// <summary>
    /// Project-window style picker for the Prefab Brush. The palette card shows what the brush paints as
    /// thumbnails with each model's share of the mix; selecting one edits its weight, scale, rotation and
    /// height. The library card lists every prefab and model in the chosen folders — click a tile to add or
    /// remove it, or drag assets in from the Project window.
    /// </summary>
    internal sealed class PrefabPaletteView : VisualElement
    {
        private const string RootsPrefKey = "WorldBuilder.PrefabPalette.Roots";
        private const string TileSizePrefKey = "WorldBuilder.PrefabPalette.TileSize";
        private const string DefaultRoots = "Assets/008_WorldBuilder,Assets/002_Prefabs,Assets/003_Art/Models";
        private const float MaxWeight = 5f;
        private const float TilePadding = 10f;

        private readonly PrefabBrushSettings settings;
        private readonly VisualElement paletteCard;
        private readonly Label paletteMeta;
        private readonly Label paletteEmpty;
        private readonly VisualElement paletteGrid = new VisualElement();
        private readonly VisualElement detail = new VisualElement();
        private readonly Foldout libraryCard;
        private readonly VisualElement libraryGrid = new VisualElement();
        private readonly Label libraryEmpty;
        private readonly List<GameObject> library = new List<GameObject>();
        private readonly List<KeyValuePair<Image, GameObject>> pendingPreviews = new List<KeyValuePair<Image, GameObject>>();

        private IVisualElementScheduledItem previewPoll;
        private string search = string.Empty;
        private float tileSize;
        private int selected = -1;
        private int signature;

        public PrefabPaletteView(PrefabBrushSettings settings)
        {
            this.settings = settings;
            tileSize = EditorPrefs.GetFloat(TileSizePrefKey, 72f);
            AssetPreview.SetPreviewTextureCacheSize(512);

            paletteCard = PrefabBrushUI.Card(PrefabBrushUI.T("card.palette"), out paletteMeta);
            paletteCard.Add(PrefabBrushUI.Hint(PrefabBrushUI.T("palette.hint")));
            paletteEmpty = new Label(PrefabBrushUI.T("palette.empty"));
            paletteEmpty.AddToClassList("pb-empty");
            paletteCard.Add(paletteEmpty);
            paletteGrid.AddToClassList("pb-grid");
            paletteCard.Add(paletteGrid);
            paletteCard.Add(detail);
            Add(paletteCard);

            libraryCard = PrefabBrushUI.FoldoutCard(PrefabBrushUI.T("card.library"), true);
            libraryEmpty = PrefabBrushUI.Hint(string.Empty);
            BuildLibrary(libraryCard);
            Add(libraryCard);

            paletteCard.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            paletteCard.RegisterCallback<DragPerformEvent>(OnDragPerform);
            paletteCard.RegisterCallback<DragLeaveEvent>(_ => paletteCard.RemoveFromClassList("pb-drop-target"));
            paletteCard.RegisterCallback<DragExitedEvent>(_ => paletteCard.RemoveFromClassList("pb-drop-target"));
            RegisterCallback<AttachToPanelEvent>(_ => Undo.undoRedoPerformed += Refresh);
            RegisterCallback<DetachFromPanelEvent>(_ => Undo.undoRedoPerformed -= Refresh);
            // Other tools (the map browser, presets, environment brushes) also edit the entry list.
            schedule.Execute(() =>
            {
                if (Signature() != signature) Refresh();
            }).Every(1000);

            ReloadLibrary();
            Refresh();
        }

        /// <summary>Rebuilds the palette, the selected model's settings and the library marks.</summary>
        public void Refresh()
        {
            signature = Signature();
            if (selected >= settings.prefabEntries.Count) selected = -1;
            RebuildPalette();
            RebuildDetail();
            RebuildLibrary();
        }

        // ------------------------------------------------------------------ palette

        private void RebuildPalette()
        {
            paletteGrid.Clear();
            float total = TotalWeight();
            List<PrefabEntry> entries = settings.prefabEntries;
            int active = 0;

            for (int i = 0; i < entries.Count; i++)
            {
                int index = i;
                PrefabEntry entry = entries[i];
                bool on = entry.prefab != null && entry.weight > 0f && total > 0f;
                if (on) active++;

                string tooltip = entry.prefab != null
                    ? AssetDatabase.GetAssetPath(entry.prefab)
                    : PrefabBrushUI.T("tip.missing");
                if (entry.envType != EnvironmentType.None) tooltip += "\n" + PrefabBrushUI.T("tip.env");

                VisualElement tile = Tile(entry.prefab,
                    entry.prefab != null ? entry.prefab.name : PrefabBrushUI.T("tile.missing"),
                    on ? $"{entry.weight / total * 100f:0}%" : PrefabBrushUI.T("tile.off"), !on, tooltip,
                    () =>
                    {
                        selected = selected == index ? -1 : index;
                        RebuildPalette();
                        RebuildDetail();
                    });
                if (index == selected) tile.AddToClassList("pb-tile--selected");
                if (!on) tile.AddToClassList("pb-tile--off");
                if (entry.envType != EnvironmentType.None) AddTag(tile, PrefabBrushUI.T("tile.env"));
                paletteGrid.Add(tile);
            }

            paletteEmpty.style.display = entries.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            paletteMeta.text = PrefabBrushUI.T("palette.meta", entries.Count, active);
        }

        private void RebuildDetail()
        {
            detail.Clear();
            if (selected < 0 || selected >= settings.prefabEntries.Count) return;

            int index = selected;
            PrefabEntry entry = settings.prefabEntries[index];

            VisualElement box = new VisualElement();
            box.AddToClassList("pb-detail");
            detail.Add(box);

            VisualElement header = new VisualElement();
            header.AddToClassList("pb-detail__header");
            box.Add(header);

            Image preview = new Image { scaleMode = ScaleMode.ScaleToFit };
            preview.AddToClassList("pb-detail__preview");
            SetPreview(preview, entry.prefab);
            header.Add(preview);

            VisualElement info = new VisualElement();
            info.AddToClassList("pb-detail__info");
            header.Add(info);

            VisualElement names = new VisualElement();
            Label nameLabel = new Label(entry.prefab != null ? entry.prefab.name : PrefabBrushUI.T("tile.missing"));
            nameLabel.AddToClassList("pb-detail__name");
            names.Add(nameLabel);
            Label path = new Label(entry.prefab != null ? AssetDatabase.GetAssetPath(entry.prefab) : string.Empty);
            path.AddToClassList("pb-detail__path");
            names.Add(path);
            info.Add(names);

            VisualElement buttons = PrefabBrushUI.Row();
            if (entry.prefab != null)
            {
                buttons.Add(PrefabBrushUI.Button(PrefabBrushUI.T("detail.ping"), () => EditorGUIUtility.PingObject(entry.prefab)));
            }
            buttons.Add(PrefabBrushUI.Button(PrefabBrushUI.T("detail.remove"), () => RemoveAt(index), "danger"));
            info.Add(buttons);

            // Weight and the share it gives this model.
            VisualElement share = new VisualElement();
            share.AddToClassList("pb-share");
            VisualElement track = new VisualElement();
            track.AddToClassList("pb-share__track");
            VisualElement fill = new VisualElement();
            fill.AddToClassList("pb-share__fill");
            track.Add(fill);
            Label shareLabel = new Label();
            shareLabel.AddToClassList("pb-share__label");
            share.Add(track);
            share.Add(shareLabel);

            void UpdateShare()
            {
                float total = TotalWeight();
                float weight = settings.prefabEntries[index].weight;
                float ratio = weight > 0f && total > 0f ? weight / total : 0f;
                fill.style.width = Length.Percent(ratio * 100f);
                shareLabel.text = ratio > 0f
                    ? PrefabBrushUI.T("detail.share", Mathf.RoundToInt(ratio * 100f))
                    : PrefabBrushUI.T("detail.shareOff");
            }

            Slider weightSlider = new Slider(PrefabBrushUI.T("detail.weight"), 0f, MaxWeight) { value = entry.weight, showInputField = true }
                .WithTooltip(PrefabBrushUI.T("detail.weightTip"));
            weightSlider.RegisterValueChangedCallback(evt =>
            {
                Modify(index, "Set Prefab Weight", e =>
                {
                    e.weight = Mathf.Max(0f, evt.newValue);
                    return e;
                });
                UpdateShare();
                RebuildPalette();
            });
            box.Add(weightSlider);
            UpdateShare();
            box.Add(share);

            FloatField height = new FloatField(PrefabBrushUI.T("detail.height")) { value = entry.heightOffset }
                .WithTooltip(PrefabBrushUI.T("detail.heightTip"));
            height.RegisterValueChangedCallback(evt => Modify(index, "Set Prefab Height Offset", e =>
            {
                e.heightOffset = evt.newValue;
                return e;
            }));
            box.Add(height);

            // Transform: shared settings or this model's own.
            VisualElement transformBox = new VisualElement();
            transformBox.AddToClassList("pb-subsection");
            box.Add(transformBox);

            Toggle custom = new Toggle(PrefabBrushUI.T("detail.custom")) { value = entry.customTransform }
                .WithTooltip(PrefabBrushUI.T("detail.customTip"));
            custom.RegisterValueChangedCallback(evt =>
            {
                Modify(index, "Toggle Prefab Transform", e =>
                {
                    if (evt.newValue && !e.customTransform)
                    {
                        // Start from the shared values so switching on never changes anything by itself.
                        e.scaleRange = settings.scaleRange;
                        e.randomYaw = settings.randomYaw;
                        e.alignToNormal = settings.alignToNormal;
                    }

                    e.customTransform = evt.newValue;
                    return e;
                });
                RebuildDetail();
            });
            transformBox.Add(custom);

            if (!entry.customTransform)
            {
                Vector2 range = settings.scaleRange;
                transformBox.Add(PrefabBrushUI.Hint(PrefabBrushUI.T("detail.inherit", range.x.ToString("0.##"),
                    range.y.ToString("0.##"), OnOff(settings.randomYaw), OnOff(settings.alignToNormal))));
                return;
            }

            MinMaxSlider scale = new MinMaxSlider(PrefabBrushUI.T("detail.scale"), entry.scaleRange.x, entry.scaleRange.y, 0.1f, 5f);
            Label scaleLabel = PrefabBrushUI.Hint(string.Empty);
            void UpdateScaleLabel(Vector2 value) =>
                scaleLabel.text = PrefabBrushUI.T("detail.scaleRange", value.x.ToString("0.##"), value.y.ToString("0.##"));
            scale.RegisterValueChangedCallback(evt =>
            {
                Vector2 value = new Vector2(Round(evt.newValue.x), Round(evt.newValue.y));
                Modify(index, "Set Prefab Scale", e =>
                {
                    e.scaleRange = value;
                    return e;
                });
                UpdateScaleLabel(value);
            });
            UpdateScaleLabel(entry.scaleRange);
            transformBox.Add(scale);
            transformBox.Add(scaleLabel);

            Toggle yaw = new Toggle(PrefabBrushUI.T("yaw")) { value = entry.randomYaw };
            yaw.RegisterValueChangedCallback(evt => Modify(index, "Set Prefab Random Yaw", e =>
            {
                e.randomYaw = evt.newValue;
                return e;
            }));
            transformBox.Add(yaw);

            Toggle align = new Toggle(PrefabBrushUI.T("align")) { value = entry.alignToNormal };
            align.RegisterValueChangedCallback(evt => Modify(index, "Set Prefab Align", e =>
            {
                e.alignToNormal = evt.newValue;
                return e;
            }));
            transformBox.Add(align);
        }

        // ------------------------------------------------------------------ library

        private void BuildLibrary(VisualElement card)
        {
            VisualElement toolbar = new VisualElement();
            toolbar.AddToClassList("pb-library-toolbar");
            card.Add(toolbar);

            ToolbarSearchField searchField = new ToolbarSearchField();
            searchField.RegisterValueChangedCallback(evt =>
            {
                search = evt.newValue ?? string.Empty;
                RebuildLibrary();
            });
            toolbar.Add(searchField);

            Slider size = new Slider(48f, 128f) { value = tileSize }.WithTooltip(PrefabBrushUI.T("library.size"));
            size.AddToClassList("pb-size-slider");
            size.RegisterValueChangedCallback(evt =>
            {
                tileSize = evt.newValue;
                EditorPrefs.SetFloat(TileSizePrefKey, tileSize);
                RebuildPalette();
                RebuildLibrary();
            });
            toolbar.Add(size);

            TextField roots = new TextField(PrefabBrushUI.T("library.folders"))
            {
                value = EditorPrefs.GetString(RootsPrefKey, DefaultRoots),
                isDelayed = true
            }.WithTooltip(PrefabBrushUI.T("library.foldersTip"));
            roots.style.flexGrow = 1f;
            roots.RegisterValueChangedCallback(evt =>
            {
                EditorPrefs.SetString(RootsPrefKey, evt.newValue);
                ReloadLibrary();
                RebuildLibrary();
            });
            Button refresh = PrefabBrushUI.Button(PrefabBrushUI.T("library.refresh"), () =>
            {
                ReloadLibrary();
                RebuildLibrary();
            });
            refresh.style.marginLeft = 4f;
            refresh.style.marginRight = 0f;
            card.Add(PrefabBrushUI.Row(roots, refresh));

            card.Add(libraryEmpty);

            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("pb-library-scroll");
            libraryGrid.AddToClassList("pb-grid");
            scroll.Add(libraryGrid);
            card.Add(scroll);
        }

        private void ReloadLibrary()
        {
            library.Clear();
            List<string> folders = new List<string>();
            foreach (string raw in EditorPrefs.GetString(RootsPrefKey, DefaultRoots).Split(','))
            {
                string folder = raw.Trim().TrimEnd('/');
                if (folder.Length > 0 && AssetDatabase.IsValidFolder(folder)) folders.Add(folder);
            }

            if (folders.Count == 0) return;

            HashSet<string> seen = new HashSet<string>();
            foreach (string filter in new[] { "t:Prefab", "t:Model" })
            {
                foreach (string guid in AssetDatabase.FindAssets(filter, folders.ToArray()))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!seen.Add(path)) continue;
                    GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (asset != null && asset.GetComponentInChildren<Renderer>(true) != null) library.Add(asset);
                }
            }

            library.Sort((a, b) => string.CompareOrdinal(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b)));
        }

        private void RebuildLibrary()
        {
            libraryGrid.Clear();
            int shown = 0;
            foreach (GameObject asset in library)
            {
                if (asset == null) continue;
                if (search.Length > 0 && asset.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;

                bool inPalette = IndexOf(asset) >= 0;
                string tooltip = AssetDatabase.GetAssetPath(asset) + "\n" +
                                 PrefabBrushUI.T(inPalette ? "library.tipRemove" : "library.tipAdd");
                VisualElement tile = Tile(asset, asset.name, inPalette ? "✓" : null, false, tooltip, () => ToggleInPalette(asset));
                if (inPalette) tile.AddToClassList("pb-tile--in-palette");
                libraryGrid.Add(tile);
                shown++;
            }

            libraryCard.text = PrefabBrushUI.T("card.library") + "  ·  " + PrefabBrushUI.T("library.meta", shown);
            libraryEmpty.text = library.Count == 0 ? PrefabBrushUI.T("library.empty") : PrefabBrushUI.T("library.noMatch");
            libraryEmpty.style.display = shown == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ------------------------------------------------------------------ edits

        private void ToggleInPalette(GameObject asset)
        {
            int index = IndexOf(asset);
            if (index >= 0)
            {
                RemoveAt(index);
                return;
            }

            AddToPalette(asset);
        }

        private void AddToPalette(GameObject asset)
        {
            if (asset == null || IndexOf(asset) >= 0) return;
            Undo.RecordObject(settings, "Add Prefab To Palette");
            settings.prefabEntries.Add(new PrefabEntry { prefab = asset, weight = 1f });
            EditorUtility.SetDirty(settings);
            selected = settings.prefabEntries.Count - 1;
            Refresh();
        }

        private void RemoveAt(int index)
        {
            if (index < 0 || index >= settings.prefabEntries.Count) return;
            Undo.RecordObject(settings, "Remove Prefab From Palette");
            settings.prefabEntries.RemoveAt(index);
            EditorUtility.SetDirty(settings);
            if (selected == index) selected = -1;
            else if (selected > index) selected--;
            Refresh();
        }

        private void Modify(int index, string undoName, Func<PrefabEntry, PrefabEntry> change)
        {
            if (index < 0 || index >= settings.prefabEntries.Count) return;
            Undo.RecordObject(settings, undoName);
            settings.prefabEntries[index] = change(settings.prefabEntries[index]);
            EditorUtility.SetDirty(settings);
            signature = Signature();
        }

        private void OnDragUpdated(DragUpdatedEvent evt)
        {
            if (DraggedModels().Count == 0) return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            paletteCard.AddToClassList("pb-drop-target");
            evt.StopPropagation();
        }

        private void OnDragPerform(DragPerformEvent evt)
        {
            paletteCard.RemoveFromClassList("pb-drop-target");
            List<GameObject> models = DraggedModels();
            if (models.Count == 0) return;
            DragAndDrop.AcceptDrag();
            foreach (GameObject model in models) AddToPalette(model);
            evt.StopPropagation();
        }

        private static List<GameObject> DraggedModels()
        {
            List<GameObject> models = new List<GameObject>();
            foreach (UnityEngine.Object dragged in DragAndDrop.objectReferences)
            {
                if (dragged is GameObject go && EditorUtility.IsPersistent(go)) models.Add(go);
            }

            return models;
        }

        // ------------------------------------------------------------------ helpers

        private int IndexOf(GameObject asset)
        {
            for (int i = 0; i < settings.prefabEntries.Count; i++)
            {
                if (settings.prefabEntries[i].prefab == asset) return i;
            }

            return -1;
        }

        /// <summary>Same total the brush uses when it rolls which model to place.</summary>
        private float TotalWeight()
        {
            float total = 0f;
            foreach (PrefabEntry entry in settings.prefabEntries)
            {
                if (entry.prefab != null && entry.weight > 0f) total += entry.weight;
            }

            return total;
        }

        private int Signature()
        {
            unchecked
            {
                int hash = settings.prefabEntries.Count;
                foreach (PrefabEntry entry in settings.prefabEntries)
                {
                    hash = hash * 31 + (entry.prefab != null ? entry.prefab.GetInstanceID() : 0);
                    hash = hash * 31 + entry.weight.GetHashCode();
                    hash = hash * 31 + entry.customTransform.GetHashCode();
                }

                return hash;
            }
        }

        private VisualElement Tile(GameObject asset, string caption, string badge, bool mutedBadge, string tooltip, Action onClick)
        {
            VisualElement tile = new VisualElement { tooltip = tooltip };
            tile.AddToClassList("pb-tile");
            tile.style.width = tileSize;

            Image image = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            image.AddToClassList("pb-tile__image");
            image.style.width = image.style.height = tileSize - TilePadding;
            SetPreview(image, asset);
            tile.Add(image);

            Label label = new Label(caption) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("pb-tile__label");
            tile.Add(label);

            if (!string.IsNullOrEmpty(badge))
            {
                Label tag = new Label(badge) { pickingMode = PickingMode.Ignore };
                tag.AddToClassList("pb-tile__badge");
                if (mutedBadge) tag.AddToClassList("pb-tile__badge--muted");
                tile.Add(tag);
            }

            tile.RegisterCallback<ClickEvent>(_ => onClick());
            return tile;
        }

        private static void AddTag(VisualElement tile, string text)
        {
            Label tag = new Label(text) { pickingMode = PickingMode.Ignore };
            tag.AddToClassList("pb-tile__tag");
            tile.Add(tag);
        }

        /// <summary>Shows the asset's rendered preview, polling until Unity finishes generating it.</summary>
        private void SetPreview(Image image, GameObject asset)
        {
            if (asset == null) return;
            Texture2D preview = AssetPreview.GetAssetPreview(asset);
            if (preview != null)
            {
                image.image = preview;
                return;
            }

            image.image = AssetPreview.GetMiniThumbnail(asset);
            pendingPreviews.Add(new KeyValuePair<Image, GameObject>(image, asset));
            previewPoll ??= schedule.Execute(PollPreviews).Every(200);
            previewPoll.Resume();
        }

        private void PollPreviews()
        {
            for (int i = pendingPreviews.Count - 1; i >= 0; i--)
            {
                Image image = pendingPreviews[i].Key;
                GameObject asset = pendingPreviews[i].Value;
                if (asset == null || image.panel == null)
                {
                    pendingPreviews.RemoveAt(i);
                    continue;
                }

                Texture2D preview = AssetPreview.GetAssetPreview(asset);
                if (preview != null)
                {
                    image.image = preview;
                    pendingPreviews.RemoveAt(i);
                }
                else if (!AssetPreview.IsLoadingAssetPreview(asset.GetInstanceID()))
                {
                    pendingPreviews.RemoveAt(i);
                }
            }

            if (pendingPreviews.Count == 0) previewPoll.Pause();
        }

        private static float Round(float value) => Mathf.Round(value * 20f) / 20f;

        private static string OnOff(bool value) => PrefabBrushUI.T(value ? "on" : "off");
    }
}
