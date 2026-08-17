using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using AO.Unity.Prototype;
using AO.Unity.Quests;
using UnityEngine;
using UnityEngine.UI;

namespace AO.Unity.AOStyle
{
    public class QuestEditorWindowView : MonoBehaviour
    {
        private enum EditorMode
        {
            None,
            Quest,
            NodeCreate,
            NodeEdit
        }

        private PrototypeUiContext _context;
        private Font _font;
        private RectTransform _questList;
        private RectTransform _flowList;
        private Text _validationText;
        private Text _flowTitle;
        private Text _drawerTitle;
        private GameObject _questEditorPanel;
        private GameObject _nodeEditorPanel;
        private EditorMode _editorMode = EditorMode.None;

        private string _selectedQuestId;
        private string _selectedNodeId;

        private InputField _questIdInput;
        private InputField _questNameInput;
        private InputField _questDescInput;
        private InputField _questStartNpcInput;
        private InputField _questMinLevelInput;
        private InputField _questMaxLevelInput;
        private Dropdown _questRepeatableDropdown;
        private InputField _questStartNodeInput;
        private Dropdown _questStartConditionModeDropdown;
        private InputField _questStartConditionsJsonInput;

        private InputField _nodeIdInput;
        private InputField _nodeTitleInput;
        private Dropdown _nodeTypeDropdown;
        private InputField _nodeDialogueInput;
        private Dropdown _nodeObjectiveDropdown;
        private InputField _nodeTargetInput;
        private InputField _nodeTargetFamilyInput;
        private InputField _nodeTargetTemplatesInput;
        private InputField _nodeItemInput;
        private InputField _nodeCountInput;
        private InputField _nodeNextCsvInput;
        private Dropdown _nodeConditionModeDropdown;
        private InputField _nodeConditionsJsonInput;
        private InputField _nodeRewardsJsonInput;
        private InputField _nodeRewardRulesJsonInput;
        private Dropdown _nodeRewardRulesFirstMatchDropdown;
        private Dropdown _startCondTypeDropdown;
        private InputField _startCondIntInput;
        private Dropdown _nodeCondTypeDropdown;
        private InputField _nodeCondIntInput;
        private Dropdown _nodeRewardTypeDropdown;
        private InputField _nodeRewardTargetInput;
        private InputField _nodeRewardAmountInput;
        private Dropdown _nodeRuleModeDropdown;
        private Dropdown _nodeRuleCondTypeDropdown;
        private InputField _nodeRuleCondIntInput;
        private Dropdown _nodeRuleRewardTypeDropdown;
        private InputField _nodeRuleRewardTargetInput;
        private InputField _nodeRuleRewardAmountInput;

        public void Initialize(PrototypeUiContext context, Font font)
        {
            _context = context;
            _font = font;
            Build();
            _context.StateChanged += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_context != null)
                _context.StateChanged -= Refresh;
            if (_questList != null)
                AOStyleUiFactoryCleanup.Clear(_questList);
            if (_flowList != null)
                AOStyleUiFactoryCleanup.Clear(_flowList);
        }

        private void Build()
        {
            var root = AOStyleUiFactory.CreatePanel("QuestEditorRoot", transform, new Color(0.08f, 0.11f, 0.16f, 0.95f));
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            var top = AOStyleUiFactory.CreatePanel("TopBar", root, new Color(0.08f, 0.12f, 0.18f, 0.95f));
            top.anchorMin = new Vector2(0f, 1f);
            top.anchorMax = new Vector2(1f, 1f);
            top.pivot = new Vector2(0.5f, 1f);
            top.sizeDelta = new Vector2(0f, 30f);
            var topLayout = top.gameObject.AddComponent<HorizontalLayoutGroup>();
            topLayout.padding = new RectOffset(4, 4, 4, 4);
            topLayout.spacing = 4f;
            topLayout.childControlHeight = true;
            topLayout.childControlWidth = false;
            topLayout.childForceExpandWidth = false;

            AOStyleUiFactory.CreateButton("NewQuest", top, "New Quest", _font, NewQuest, 84f);
            AOStyleUiFactory.CreateButton("EditQuest", top, "Edit Quest", _font, EditQuest, 84f);
            AOStyleUiFactory.CreateButton("AddNode", top, "Add Node", _font, AddNode, 74f);
            AOStyleUiFactory.CreateButton("EditNode", top, "Edit Node", _font, EditNode, 74f);
            AOStyleUiFactory.CreateButton("DeleteNode", top, "Delete Node", _font, DeleteNode, 86f);
            AOStyleUiFactory.CreateButton("DeleteQuest", top, "Delete Quest", _font, DeleteQuest, 90f);
            AOStyleUiFactory.CreateButton("Save", top, "Save", _font, Save, 56f);
            AOStyleUiFactory.CreateButton("Reload", top, "Reload", _font, Reload, 60f);
            AOStyleUiFactory.CreateButton("Validate", top, "Validate", _font, Validate, 66f);
            AOStyleUiFactory.CreateButton("StartQuest", top, "Start", _font, StartQuest, 54f);

            var drawer = AOStyleUiFactory.CreatePanel("BottomEditorDrawer", root, new Color(0.06f, 0.09f, 0.13f, 0.98f));
            drawer.anchorMin = new Vector2(0f, 0f);
            drawer.anchorMax = new Vector2(1f, 0f);
            drawer.pivot = new Vector2(0.5f, 0f);
            drawer.sizeDelta = new Vector2(0f, 280f);

            _drawerTitle = AOStyleUiFactory.CreateText(
                "DrawerTitle",
                drawer,
                "Editor: none (select a quest, then click Edit Quest or Add Node)",
                _font,
                12,
                TextAnchor.MiddleLeft);
            var drawerTitleRt = (RectTransform)_drawerTitle.transform;
            drawerTitleRt.anchorMin = new Vector2(0f, 1f);
            drawerTitleRt.anchorMax = new Vector2(1f, 1f);
            drawerTitleRt.pivot = new Vector2(0.5f, 1f);
            drawerTitleRt.offsetMin = new Vector2(8f, -26f);
            drawerTitleRt.offsetMax = new Vector2(-8f, -4f);

            var drawerContent = AOStyleUiFactory.CreatePanel("DrawerContent", drawer, new Color(0f, 0f, 0f, 0f));
            drawerContent.anchorMin = new Vector2(0f, 0f);
            drawerContent.anchorMax = new Vector2(1f, 1f);
            drawerContent.offsetMin = new Vector2(6f, 6f);
            drawerContent.offsetMax = new Vector2(-6f, -30f);
            var drawerContentLayout = drawerContent.gameObject.AddComponent<HorizontalLayoutGroup>();
            drawerContentLayout.padding = new RectOffset(0, 0, 0, 0);
            drawerContentLayout.spacing = 0f;
            drawerContentLayout.childControlWidth = true;
            drawerContentLayout.childControlHeight = true;
            drawerContentLayout.childForceExpandWidth = true;
            drawerContentLayout.childForceExpandHeight = true;

            _questEditorPanel = BuildQuestEditorPanel(drawerContent);
            _nodeEditorPanel = BuildNodeEditorPanel(drawerContent);

            var body = AOStyleUiFactory.CreatePanel("Body", root, new Color(0f, 0f, 0f, 0f));
            body.anchorMin = new Vector2(0f, 0f);
            body.anchorMax = new Vector2(1f, 1f);
            body.offsetMin = new Vector2(0f, 308f);
            body.offsetMax = new Vector2(0f, -30f);
            var bodyLayout = body.gameObject.AddComponent<HorizontalLayoutGroup>();
            bodyLayout.padding = new RectOffset(4, 4, 4, 4);
            bodyLayout.spacing = 6f;
            bodyLayout.childControlHeight = true;
            bodyLayout.childControlWidth = true;
            bodyLayout.childForceExpandWidth = true;

            BuildQuestColumn(body);
            BuildFlowColumn(body);

            var bottom = AOStyleUiFactory.CreatePanel("Bottom", root, new Color(0.07f, 0.1f, 0.15f, 0.95f));
            bottom.anchorMin = new Vector2(0f, 0f);
            bottom.anchorMax = new Vector2(1f, 0f);
            bottom.pivot = new Vector2(0.5f, 0f);
            bottom.sizeDelta = new Vector2(0f, 24f);
            _validationText = AOStyleUiFactory.CreateText("Validation", bottom, "Ready.", _font, 11, TextAnchor.MiddleLeft);
            var vr = (RectTransform)_validationText.transform;
            vr.anchorMin = Vector2.zero;
            vr.anchorMax = Vector2.one;
            vr.offsetMin = new Vector2(6f, 2f);
            vr.offsetMax = new Vector2(-6f, -2f);

            SetEditorMode(EditorMode.None);
        }

        private void BuildQuestColumn(Transform parent)
        {
            var col = AOStyleUiFactory.CreatePanel("QuestColumn", parent, new Color(0.06f, 0.08f, 0.12f, 0.95f));
            col.gameObject.AddComponent<LayoutElement>().preferredWidth = 280f;
            AOStyleUiFactory.CreateText("Title", col, "Quests", _font, 12, TextAnchor.UpperLeft);
            _questList = AOStyleUiFactory.CreateScrollContent(col);
            _questList.anchorMin = new Vector2(0f, 0f);
            _questList.anchorMax = new Vector2(1f, 1f);
            _questList.offsetMin = new Vector2(2f, 2f);
            _questList.offsetMax = new Vector2(-2f, -20f);
        }

        private void BuildFlowColumn(Transform parent)
        {
            var col = AOStyleUiFactory.CreatePanel("FlowColumn", parent, new Color(0.06f, 0.08f, 0.12f, 0.95f));
            col.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            _flowTitle = AOStyleUiFactory.CreateText("FlowTitle", col, "Quest Flow", _font, 12, TextAnchor.UpperLeft);
            var titleRt = (RectTransform)_flowTitle.transform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.offsetMin = new Vector2(6f, -22f);
            titleRt.offsetMax = new Vector2(-6f, -2f);

            _flowList = AOStyleUiFactory.CreateScrollContent(col);
            _flowList.anchorMin = new Vector2(0f, 0f);
            _flowList.anchorMax = new Vector2(1f, 1f);
            _flowList.offsetMin = new Vector2(2f, 2f);
            _flowList.offsetMax = new Vector2(-2f, -24f);
        }

        private GameObject BuildQuestEditorPanel(Transform parent)
        {
            var panel = AOStyleUiFactory.CreatePanel("QuestEditorPanel", parent, new Color(0.07f, 0.1f, 0.15f, 0.95f));
            panel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var scroll = AOStyleUiFactory.CreateScrollContent(panel);
            scroll.anchorMin = new Vector2(0f, 0f);
            scroll.anchorMax = new Vector2(1f, 1f);
            scroll.offsetMin = new Vector2(2f, 2f);
            scroll.offsetMax = new Vector2(-2f, -2f);

            _questIdInput = AddField(scroll, "QuestId", "Quest ID");
            _questNameInput = AddField(scroll, "QuestName", "Name");
            _questDescInput = AddField(scroll, "QuestDesc", "Description");
            _questStartNpcInput = AddField(scroll, "StartNpc", "Start NPC ID");
            _questMinLevelInput = AddField(scroll, "MinLevel", "Min Level");
            _questMaxLevelInput = AddField(scroll, "MaxLevel", "Max Level");
            _questRepeatableDropdown = AddDropdownField(scroll, "Repeatable", "Repeatable", new List<string> { "False", "True" });
            _questStartNodeInput = AddField(scroll, "StartNode", "Start Node ID");
            _questStartConditionModeDropdown = AddDropdownField(scroll, "StartCondMode", "Start Condition Mode", new List<string> { "All", "Any" });
            _questStartConditionsJsonInput = AddField(scroll, "StartConds", "Start Conditions JSON (advanced)");
            BuildConditionHelperRow(
                scroll,
                "StartCondHelper",
                out _startCondTypeDropdown,
                out _startCondIntInput,
                AddStartConditionFromHelper);
            AOStyleUiFactory.CreateText("StartCondHint", scroll, "Tip: start with one condition like [{\"Type\":\"FactionEquals\",\"IntValue\":1}]", _font, 10, TextAnchor.MiddleLeft)
                .gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;
            AOStyleUiFactory.CreateButton("ApplyQuest", scroll, "Apply Quest", _font, ApplyQuest, 120f);
            return panel.gameObject;
        }

        private GameObject BuildNodeEditorPanel(Transform parent)
        {
            var panel = AOStyleUiFactory.CreatePanel("NodeEditorPanel", parent, new Color(0.07f, 0.1f, 0.15f, 0.95f));
            panel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var scroll = AOStyleUiFactory.CreateScrollContent(panel);
            scroll.anchorMin = new Vector2(0f, 0f);
            scroll.anchorMax = new Vector2(1f, 1f);
            scroll.offsetMin = new Vector2(2f, 2f);
            scroll.offsetMax = new Vector2(-2f, -2f);

            _nodeIdInput = AddField(scroll, "NodeId", "Node ID");
            _nodeTitleInput = AddField(scroll, "NodeTitle", "Title");
            _nodeTypeDropdown = AddDropdownField(scroll, "NodeType", "NodeType", Enum.GetNames(typeof(QuestNodeType)).ToList());
            _nodeDialogueInput = AddField(scroll, "NodeDialogue", "Dialogue");
            _nodeObjectiveDropdown = AddDropdownField(scroll, "NodeObjective", "ObjectiveType", Enum.GetNames(typeof(QuestObjectiveType)).ToList());
            _nodeTargetInput = AddField(scroll, "NodeTarget", "Target ID");
            _nodeTargetFamilyInput = AddField(scroll, "NodeTargetFamily", "Target Family ID");
            _nodeTargetTemplatesInput = AddField(scroll, "NodeTargetTemplates", "Target Template IDs (csv)");
            _nodeItemInput = AddField(scroll, "NodeItem", "Item ID");
            _nodeCountInput = AddField(scroll, "NodeCount", "Required Count");
            _nodeNextCsvInput = AddField(scroll, "NodeNext", "Next Node IDs (csv)");
            _nodeConditionModeDropdown = AddDropdownField(scroll, "NodeCondMode", "Node Condition Mode", new List<string> { "All", "Any" });
            _nodeConditionsJsonInput = AddField(scroll, "NodeConds", "Node Conditions JSON (advanced)");
            BuildConditionHelperRow(
                scroll,
                "NodeCondHelper",
                out _nodeCondTypeDropdown,
                out _nodeCondIntInput,
                AddNodeConditionFromHelper);
            _nodeRewardsJsonInput = AddField(scroll, "NodeRewards", "Node Rewards JSON (advanced)");
            BuildRewardHelperRow(scroll);
            _nodeRewardRulesJsonInput = AddField(scroll, "NodeRewardRules", "Reward Rules JSON (advanced)");
            BuildRewardRuleHelperRow(scroll);
            _nodeRewardRulesFirstMatchDropdown = AddDropdownField(scroll, "NodeRuleFirst", "RewardRules FirstMatchOnly", new List<string> { "True", "False" });
            AOStyleUiFactory.CreateText("NodeHint", scroll, "For new node: set NodeType + fields, then Apply Node.", _font, 10, TextAnchor.MiddleLeft)
                .gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;
            AOStyleUiFactory.CreateButton("ApplyNode", scroll, "Apply Node", _font, ApplyNode, 120f);
            return panel.gameObject;
        }

        private void BuildConditionHelperRow(
            Transform parent,
            string key,
            out Dropdown typeDropdown,
            out InputField intValueInput,
            Action onAdd)
        {
            var row = AOStyleUiFactory.CreatePanel($"Row_{key}", parent, new Color(0f, 0f, 0f, 0f));
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.spacing = 4f;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = false;
            AOStyleUiFactory.CreateText("Label", row, "Helper", _font, 11, TextAnchor.MiddleLeft)
                .gameObject.AddComponent<LayoutElement>().preferredWidth = 190f;
            typeDropdown = CreateDropdown($"{key}_Type", row, 180f, Enum.GetNames(typeof(QuestConditionType)).ToList());
            intValueInput = AOStyleUiFactory.CreateInputField($"{key}_Int", row, "IntValue", _font, 90f);
            AOStyleUiFactory.CreateButton($"{key}_Add", row, "Add", _font, () => onAdd?.Invoke(), 48f);
        }

        private void BuildRewardHelperRow(Transform parent)
        {
            var row = AOStyleUiFactory.CreatePanel("Row_NodeRewardHelper", parent, new Color(0f, 0f, 0f, 0f));
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.spacing = 4f;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = false;
            AOStyleUiFactory.CreateText("Label", row, "Reward Helper", _font, 11, TextAnchor.MiddleLeft)
                .gameObject.AddComponent<LayoutElement>().preferredWidth = 190f;
            _nodeRewardTypeDropdown = CreateDropdown("NodeRewardType", row, 140f, Enum.GetNames(typeof(QuestRewardType)).ToList());
            _nodeRewardTargetInput = AOStyleUiFactory.CreateInputField("NodeRewardTarget", row, "Target", _font, 70f);
            _nodeRewardAmountInput = AOStyleUiFactory.CreateInputField("NodeRewardAmount", row, "Amount", _font, 70f);
            AOStyleUiFactory.CreateButton("NodeRewardAdd", row, "Add", _font, AddNodeRewardFromHelper, 48f);
        }

        private void BuildRewardRuleHelperRow(Transform parent)
        {
            var row = AOStyleUiFactory.CreatePanel("Row_NodeRewardRuleHelper", parent, new Color(0f, 0f, 0f, 0f));
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.spacing = 3f;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = false;
            AOStyleUiFactory.CreateText("Label", row, "Rule Helper", _font, 11, TextAnchor.MiddleLeft)
                .gameObject.AddComponent<LayoutElement>().preferredWidth = 190f;

            _nodeRuleModeDropdown = CreateDropdown("NodeRuleMode", row, 62f, new List<string> { "All", "Any" });
            _nodeRuleCondTypeDropdown = CreateDropdown("NodeRuleCondType", row, 130f, Enum.GetNames(typeof(QuestConditionType)).ToList());
            _nodeRuleCondIntInput = AOStyleUiFactory.CreateInputField("NodeRuleCondInt", row, "Cond", _font, 56f);
            _nodeRuleRewardTypeDropdown = CreateDropdown("NodeRuleRewardType", row, 110f, Enum.GetNames(typeof(QuestRewardType)).ToList());
            _nodeRuleRewardTargetInput = AOStyleUiFactory.CreateInputField("NodeRuleRewardTarget", row, "Tgt", _font, 50f);
            _nodeRuleRewardAmountInput = AOStyleUiFactory.CreateInputField("NodeRuleRewardAmount", row, "Amt", _font, 50f);
            AOStyleUiFactory.CreateButton("NodeRewardRuleAdd", row, "Add", _font, AddNodeRewardRuleFromHelper, 45f);
        }

        private InputField AddField(Transform parent, string key, string placeholder)
        {
            var row = AOStyleUiFactory.CreatePanel($"Row_{key}", parent, new Color(0f, 0f, 0f, 0f));
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(0, 0, 0, 0);
            h.spacing = 4f;
            h.childControlWidth = false;
            h.childForceExpandWidth = false;
            AOStyleUiFactory.CreateText("Label", row, placeholder, _font, 11, TextAnchor.MiddleLeft).gameObject.AddComponent<LayoutElement>().preferredWidth = 190f;
            return AOStyleUiFactory.CreateInputField("Input", row, "", _font, 420f);
        }

        private Dropdown AddDropdownField(Transform parent, string key, string label, List<string> options)
        {
            var row = AOStyleUiFactory.CreatePanel($"Row_{key}", parent, new Color(0f, 0f, 0f, 0f));
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(0, 0, 0, 0);
            h.spacing = 4f;
            h.childControlWidth = false;
            h.childForceExpandWidth = false;
            AOStyleUiFactory.CreateText("Label", row, label, _font, 11, TextAnchor.MiddleLeft).gameObject.AddComponent<LayoutElement>().preferredWidth = 190f;
            return CreateDropdown($"{key}_Dropdown", row, 240f, options);
        }

        private Dropdown CreateDropdown(string name, Transform parent, float width, List<string> options)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Dropdown));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.16f, 0.2f, 0.28f, 1f);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = 22f;

            var dropdown = go.GetComponent<Dropdown>();
            var label = AOStyleUiFactory.CreateText("Label", go.transform, "", _font, 11, TextAnchor.MiddleLeft);
            var labelRt = (RectTransform)label.transform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(6f, 2f);
            labelRt.offsetMax = new Vector2(-22f, -2f);
            var arrow = AOStyleUiFactory.CreateText("Arrow", go.transform, "v", _font, 11, TextAnchor.MiddleCenter);
            var arrowRt = (RectTransform)arrow.transform;
            arrowRt.anchorMin = new Vector2(1f, 0f);
            arrowRt.anchorMax = new Vector2(1f, 1f);
            arrowRt.pivot = new Vector2(1f, 0.5f);
            arrowRt.sizeDelta = new Vector2(18f, 0f);
            arrowRt.anchoredPosition = new Vector2(-4f, 0f);

            var template = AOStyleUiFactory.CreatePanel("Template", go.transform, new Color(0.07f, 0.09f, 0.13f, 0.98f));
            template.gameObject.SetActive(false);
            template.anchorMin = new Vector2(0f, 0f);
            template.anchorMax = new Vector2(1f, 0f);
            template.pivot = new Vector2(0.5f, 1f);
            template.anchoredPosition = new Vector2(0f, -2f);
            template.sizeDelta = new Vector2(0f, 176f);
            template.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var scrollRect = template.gameObject.AddComponent<ScrollRect>();
            scrollRect.vertical = true;
            scrollRect.horizontal = false;

            var viewport = AOStyleUiFactory.CreatePanel("Viewport", template, new Color(0f, 0f, 0f, 0.01f));
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport, false);
            var contentRt = (RectTransform)content.transform;
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.offsetMin = Vector2.zero;
            contentRt.offsetMax = Vector2.zero;
            var contentLayout = content.GetComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 0f;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var item = AOStyleUiFactory.CreatePanel("Item", contentRt, new Color(0.16f, 0.2f, 0.28f, 1f));
            item.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
            item.gameObject.AddComponent<Toggle>();
            var itemLabel = AOStyleUiFactory.CreateText("ItemLabel", item, "Option", _font, 11, TextAnchor.MiddleLeft);
            var itemLabelRt = (RectTransform)itemLabel.transform;
            itemLabelRt.anchorMin = Vector2.zero;
            itemLabelRt.anchorMax = Vector2.one;
            itemLabelRt.offsetMin = new Vector2(6f, 0f);
            itemLabelRt.offsetMax = new Vector2(-6f, 0f);

            dropdown.targetGraphic = go.GetComponent<Image>();
            dropdown.template = template;
            dropdown.captionText = label;
            dropdown.itemText = itemLabel;
            dropdown.options = options?.Select(o => new Dropdown.OptionData(o)).ToList() ?? new List<Dropdown.OptionData>();
            scrollRect.viewport = viewport;
            scrollRect.content = contentRt;

            if (dropdown.options.Count > 0)
                dropdown.value = 0;
            dropdown.RefreshShownValue();
            return dropdown;
        }

        private static string DropdownText(Dropdown dropdown, string fallback = "")
        {
            if (dropdown == null || dropdown.options == null || dropdown.options.Count == 0)
                return fallback;
            int index = Mathf.Clamp(dropdown.value, 0, dropdown.options.Count - 1);
            return dropdown.options[index].text ?? fallback;
        }

        private static void SetDropdownValue(Dropdown dropdown, string text)
        {
            if (dropdown == null || dropdown.options == null || dropdown.options.Count == 0)
                return;
            int idx = dropdown.options.FindIndex(o => string.Equals(o.text, text, StringComparison.OrdinalIgnoreCase));
            dropdown.value = idx >= 0 ? idx : 0;
            dropdown.RefreshShownValue();
        }

        private void Refresh()
        {
            if (_context == null)
                return;
            if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
                return;

            RebuildQuestList();
            RebuildFlowTree();
            PopulateEditorFields();
        }

        private void RebuildQuestList()
        {
            AOStyleUiFactoryCleanup.Clear(_questList);
            var quests = _context.Quests ?? Array.Empty<QuestDefinition>();
            foreach (var quest in quests)
            {
                if (quest == null)
                    continue;

                var row = AOStyleUiFactory.CreatePanel("QuestRow", _questList, new Color(0.11f, 0.15f, 0.21f, 0.95f));
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
                var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                rowLayout.padding = new RectOffset(2, 2, 1, 1);
                rowLayout.spacing = 2f;
                rowLayout.childControlWidth = true;
                rowLayout.childForceExpandWidth = true;
                var btn = AOStyleUiFactory.CreateButton(
                    "Select",
                    row,
                    $"{quest.QuestId} - {(string.IsNullOrWhiteSpace(quest.Name) ? "(unnamed)" : quest.Name)}",
                    _font,
                    () =>
                    {
                        _selectedQuestId = quest.QuestId;
                        _selectedNodeId = null;
                        Refresh();
                    },
                    10f);
                var rowImage = row.GetComponent<Image>();
                if (!string.IsNullOrWhiteSpace(_selectedQuestId) && string.Equals(_selectedQuestId, quest.QuestId, StringComparison.OrdinalIgnoreCase))
                    rowImage.color = new Color(0.24f, 0.37f, 0.54f, 0.98f);
                var btnLe = btn.gameObject.GetComponent<LayoutElement>();
                btnLe.preferredHeight = 20f;
                btnLe.flexibleWidth = 1f;
            }
        }

        private void RebuildFlowTree()
        {
            AOStyleUiFactoryCleanup.Clear(_flowList);
            var quest = GetSelectedQuest();
            if (quest == null || quest.Nodes == null || quest.Nodes.Count == 0)
            {
                _flowTitle.text = "Quest Flow";
                AOStyleUiFactory.CreateText("None", _flowList, "Select a quest to view node flow.", _font, 11, TextAnchor.MiddleLeft)
                    .gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
                return;
            }

            _flowTitle.text = $"Quest Flow: {quest.QuestId} (Start: {quest.StartNodeId})";
            var byId = new Dictionary<string, QuestNode>(StringComparer.OrdinalIgnoreCase);
            int duplicateCount = 0;
            for (int i = 0; i < quest.Nodes.Count; i++)
            {
                var node = quest.Nodes[i];
                if (node == null || string.IsNullOrWhiteSpace(node.NodeId))
                    continue;

                string key = node.NodeId.Trim();
                if (byId.ContainsKey(key))
                {
                    duplicateCount++;
                    continue;
                }

                byId[key] = node;
            }

            var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            EmitFlowNode(quest.StartNodeId, 0, byId, emitted);

            var unlinked = byId.Keys.Where(id => !emitted.Contains(id)).OrderBy(id => id).ToList();
            if (unlinked.Count > 0)
            {
                AOStyleUiFactory.CreateText("UnlinkedTitle", _flowList, "Unlinked nodes:", _font, 11, TextAnchor.MiddleLeft)
                    .gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
                for (int i = 0; i < unlinked.Count; i++)
                    EmitFlowNode(unlinked[i], 1, byId, emitted, traverseChildren: false);
            }

            if (duplicateCount > 0)
            {
                AOStyleUiFactory.CreateText(
                    "DupWarn",
                    _flowList,
                    $"Warning: {duplicateCount} duplicate node id(s) hidden from flow. Rename duplicates.",
                    _font,
                    11,
                    TextAnchor.MiddleLeft).gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
            }
        }

        private void EmitFlowNode(
            string nodeId,
            int depth,
            IReadOnlyDictionary<string, QuestNode> byId,
            HashSet<string> emitted,
            bool traverseChildren = true)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
                return;
            if (!byId.TryGetValue(nodeId, out var node))
                return;

            string indent = new string(' ', Mathf.Clamp(depth, 0, 24) * 2);
            string next = node.NextNodeIds == null || node.NextNodeIds.Count == 0
                ? "-"
                : string.Join(", ", node.NextNodeIds.Where(n => !string.IsNullOrWhiteSpace(n)));
            string label = $"{indent}* {node.NodeId} [{node.NodeType}] -> {next}";

            var row = AOStyleUiFactory.CreatePanel("FlowRow", _flowList, new Color(0.11f, 0.15f, 0.21f, 0.95f));
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
            var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.padding = new RectOffset(2, 2, 1, 1);
            rowLayout.spacing = 2f;
            rowLayout.childControlWidth = true;
            rowLayout.childForceExpandWidth = true;
            var btn = AOStyleUiFactory.CreateButton("Select", row, label, _font, () =>
            {
                _selectedNodeId = node.NodeId;
                if (_editorMode == EditorMode.NodeEdit)
                    PopulateEditorFields();
                Refresh();
            }, 10f);
            var btnLe = btn.gameObject.GetComponent<LayoutElement>();
            btnLe.preferredHeight = 18f;
            btnLe.flexibleWidth = 1f;
            var rowImage = row.GetComponent<Image>();
            if (!string.IsNullOrWhiteSpace(_selectedNodeId) && string.Equals(_selectedNodeId, node.NodeId, StringComparison.OrdinalIgnoreCase))
                rowImage.color = new Color(0.24f, 0.37f, 0.54f, 0.98f);

            if (!emitted.Add(node.NodeId))
                return;
            if (!traverseChildren || node.NextNodeIds == null)
                return;

            for (int i = 0; i < node.NextNodeIds.Count; i++)
                EmitFlowNode(node.NextNodeIds[i], depth + 1, byId, emitted);
        }

        private void SetEditorMode(EditorMode mode)
        {
            _editorMode = mode;
            _questEditorPanel.SetActive(mode == EditorMode.Quest);
            _nodeEditorPanel.SetActive(mode == EditorMode.NodeCreate || mode == EditorMode.NodeEdit);
            _drawerTitle.text = mode switch
            {
                EditorMode.Quest => "Editor: Quest Details",
                EditorMode.NodeCreate => "Editor: New Node",
                EditorMode.NodeEdit => "Editor: Edit Node",
                _ => "Editor: none (select a quest, then click Edit Quest or Add Node)"
            };
        }

        private void PopulateEditorFields()
        {
            var quest = GetSelectedQuest();
            if (quest != null)
            {
                _questIdInput.text = quest.QuestId ?? string.Empty;
                _questNameInput.text = quest.Name ?? string.Empty;
                _questDescInput.text = quest.Description ?? string.Empty;
                _questStartNpcInput.text = quest.StartNpcId.ToString();
                _questMinLevelInput.text = quest.MinLevel.ToString();
                _questMaxLevelInput.text = quest.MaxLevel.ToString();
                SetDropdownValue(_questRepeatableDropdown, quest.Repeatable ? "True" : "False");
                _questStartNodeInput.text = quest.StartNodeId ?? string.Empty;
                SetDropdownValue(_questStartConditionModeDropdown, quest.StartConditionMode.ToString());
                _questStartConditionsJsonInput.text = SerializeJson(quest.StartConditions);
            }

            var node = GetSelectedNode();
            if (node != null)
            {
                _nodeIdInput.text = node.NodeId ?? string.Empty;
                _nodeTitleInput.text = node.Title ?? string.Empty;
                SetDropdownValue(_nodeTypeDropdown, node.NodeType.ToString());
                _nodeDialogueInput.text = node.DialogueText ?? string.Empty;
                SetDropdownValue(_nodeObjectiveDropdown, node.ObjectiveType.ToString());
                _nodeTargetInput.text = node.TargetId.ToString();
                _nodeTargetFamilyInput.text = node.TargetFamilyId ?? string.Empty;
                _nodeTargetTemplatesInput.text = string.Join(",", node.TargetTemplateIds ?? new List<int>());
                _nodeItemInput.text = node.ItemId.ToString();
                _nodeCountInput.text = node.RequiredCount.ToString();
                _nodeNextCsvInput.text = string.Join(",", node.NextNodeIds ?? new List<string>());
                SetDropdownValue(_nodeConditionModeDropdown, node.ConditionMode.ToString());
                _nodeConditionsJsonInput.text = SerializeJson(node.Conditions);
                _nodeRewardsJsonInput.text = SerializeJson(node.Rewards);
                _nodeRewardRulesJsonInput.text = SerializeJson(node.RewardRules);
                SetDropdownValue(_nodeRewardRulesFirstMatchDropdown, node.RewardRulesFirstMatchOnly ? "True" : "False");
            }
        }

        private QuestDefinition GetSelectedQuest()
        {
            if (_context?.Quests == null)
                return null;
            if (string.IsNullOrWhiteSpace(_selectedQuestId))
                return _context.Quests.FirstOrDefault();
            return _context.Quests.FirstOrDefault(q => q != null && string.Equals(q.QuestId, _selectedQuestId, StringComparison.OrdinalIgnoreCase));
        }

        private QuestNode GetSelectedNode()
        {
            var quest = GetSelectedQuest();
            if (quest?.Nodes == null)
                return null;
            if (string.IsNullOrWhiteSpace(_selectedNodeId))
                return quest.Nodes.FirstOrDefault();
            return quest.Nodes.FirstOrDefault(n => n != null && string.Equals(n.NodeId, _selectedNodeId, StringComparison.OrdinalIgnoreCase));
        }

        private void NewQuest()
        {
            var quest = _context.CreateQuest();
            _selectedQuestId = quest?.QuestId;
            _selectedNodeId = quest?.StartNodeId;
            SetEditorMode(EditorMode.Quest);
            Refresh();
        }

        private void EditQuest()
        {
            if (GetSelectedQuest() == null)
            {
                _validationText.text = "Select a quest first.";
                return;
            }

            SetEditorMode(EditorMode.Quest);
            PopulateEditorFields();
        }

        private void AddNode()
        {
            var quest = GetSelectedQuest();
            if (quest == null)
            {
                _validationText.text = "Select a quest first.";
                return;
            }

            _nodeIdInput.text = $"node_{(quest.Nodes?.Count ?? 0) + 1}";
            _nodeTitleInput.text = string.Empty;
            SetDropdownValue(_nodeTypeDropdown, QuestNodeType.Dialogue.ToString());
            _nodeDialogueInput.text = string.Empty;
            SetDropdownValue(_nodeObjectiveDropdown, QuestObjectiveType.None.ToString());
            _nodeTargetInput.text = "0";
            _nodeTargetFamilyInput.text = string.Empty;
            _nodeTargetTemplatesInput.text = string.Empty;
            _nodeItemInput.text = "0";
            _nodeCountInput.text = "0";
            _nodeNextCsvInput.text = string.Empty;
            SetDropdownValue(_nodeConditionModeDropdown, QuestConditionMode.All.ToString());
            _nodeConditionsJsonInput.text = "[]";
            _nodeRewardsJsonInput.text = "[]";
            _nodeRewardRulesJsonInput.text = "[]";
            SetDropdownValue(_nodeRewardRulesFirstMatchDropdown, "True");
            SetEditorMode(EditorMode.NodeCreate);
        }

        private void EditNode()
        {
            if (GetSelectedNode() == null)
            {
                _validationText.text = "Select a node from Quest Flow first.";
                return;
            }

            SetEditorMode(EditorMode.NodeEdit);
            PopulateEditorFields();
        }

        private void DeleteNode()
        {
            var quest = GetSelectedQuest();
            var node = GetSelectedNode();
            if (quest == null || node == null)
            {
                _validationText.text = "Select a quest and node first.";
                return;
            }

            if (_context.DeleteQuestNode(quest.QuestId, node.NodeId))
            {
                _selectedNodeId = quest.Nodes.FirstOrDefault()?.NodeId;
                _validationText.text = $"Deleted node '{node.NodeId}'.";
                Refresh();
            }
        }

        private void DeleteQuest()
        {
            if (string.IsNullOrWhiteSpace(_selectedQuestId))
            {
                _validationText.text = "Select a quest first.";
                return;
            }

            if (_context.DeleteQuest(_selectedQuestId))
            {
                _selectedQuestId = _context.Quests.FirstOrDefault()?.QuestId;
                _selectedNodeId = null;
                _validationText.text = "Quest deleted.";
                SetEditorMode(EditorMode.None);
                Refresh();
            }
        }

        private void ApplyQuest()
        {
            var quest = GetSelectedQuest();
            if (quest == null)
            {
                _validationText.text = "No quest selected.";
                return;
            }

            quest.QuestId = Safe(_questIdInput.text, quest.QuestId);
            quest.Name = Safe(_questNameInput.text, quest.Name);
            quest.Description = _questDescInput.text ?? string.Empty;
            quest.StartNpcId = ParseInt(_questStartNpcInput.text, quest.StartNpcId);
            quest.MinLevel = Mathf.Max(1, ParseInt(_questMinLevelInput.text, quest.MinLevel));
            quest.MaxLevel = Mathf.Max(quest.MinLevel, ParseInt(_questMaxLevelInput.text, quest.MaxLevel));
            quest.Repeatable = string.Equals(DropdownText(_questRepeatableDropdown, "False"), "True", StringComparison.OrdinalIgnoreCase);
            quest.StartNodeId = Safe(_questStartNodeInput.text, quest.StartNodeId);
            quest.StartConditionMode = ParseEnum(DropdownText(_questStartConditionModeDropdown, quest.StartConditionMode.ToString()), quest.StartConditionMode);
            quest.StartConditions = ParseJsonList(_questStartConditionsJsonInput.text, quest.StartConditions);
            _selectedQuestId = quest.QuestId;
            _context.MarkQuestsDirty($"Updated quest '{quest.QuestId}'.");
            _validationText.text = $"Quest '{quest.QuestId}' updated.";
            Refresh();
        }

        private void ApplyNode()
        {
            var quest = GetSelectedQuest();
            if (quest == null)
            {
                _validationText.text = "Select a quest first.";
                return;
            }

            QuestNode node;
            if (_editorMode == EditorMode.NodeCreate)
            {
                var nodeTypeCreate = ParseEnum(DropdownText(_nodeTypeDropdown, QuestNodeType.Dialogue.ToString()), QuestNodeType.Dialogue);
                node = _context.CreateQuestNode(quest.QuestId, nodeTypeCreate);
                if (node == null)
                {
                    _validationText.text = "Failed to create node.";
                    return;
                }
            }
            else
            {
                node = GetSelectedNode();
                if (node == null)
                {
                    _validationText.text = "Select a node first.";
                    return;
                }
            }

            string requestedNodeId = Safe(_nodeIdInput.text, node.NodeId);
            bool duplicateId = quest.Nodes != null
                && quest.Nodes.Any(n => n != null
                    && !ReferenceEquals(n, node)
                    && string.Equals((n.NodeId ?? string.Empty).Trim(), requestedNodeId, StringComparison.OrdinalIgnoreCase));
            if (duplicateId)
            {
                _validationText.text = $"Node id '{requestedNodeId}' already exists in this quest. Use a unique node id.";
                return;
            }

            node.NodeId = requestedNodeId;
            node.Title = Safe(_nodeTitleInput.text, node.Title);
            node.DialogueText = _nodeDialogueInput.text ?? string.Empty;
            node.NodeType = ParseEnum(DropdownText(_nodeTypeDropdown, node.NodeType.ToString()), node.NodeType);
            node.ObjectiveType = ParseEnum(DropdownText(_nodeObjectiveDropdown, node.ObjectiveType.ToString()), node.ObjectiveType);
            node.TargetId = ParseInt(_nodeTargetInput.text, node.TargetId);
            node.TargetFamilyId = (_nodeTargetFamilyInput.text ?? string.Empty).Trim();
            node.TargetTemplateIds = ParseIntCsv(_nodeTargetTemplatesInput.text);
            node.ItemId = ParseInt(_nodeItemInput.text, node.ItemId);
            node.RequiredCount = ParseInt(_nodeCountInput.text, node.RequiredCount);
            node.NextNodeIds = ParseCsv(_nodeNextCsvInput.text);
            node.ConditionMode = ParseEnum(DropdownText(_nodeConditionModeDropdown, node.ConditionMode.ToString()), node.ConditionMode);
            node.Conditions = ParseJsonList(_nodeConditionsJsonInput.text, node.Conditions);
            node.Rewards = ParseJsonList(_nodeRewardsJsonInput.text, node.Rewards);
            node.RewardRules = ParseJsonList(_nodeRewardRulesJsonInput.text, node.RewardRules);
            node.RewardRulesFirstMatchOnly = string.Equals(DropdownText(_nodeRewardRulesFirstMatchDropdown, "True"), "True", StringComparison.OrdinalIgnoreCase);

            _selectedNodeId = node.NodeId;
            SetEditorMode(EditorMode.NodeEdit);
            _context.MarkQuestsDirty($"Updated node '{node.NodeId}'.");
            _validationText.text = $"Node '{node.NodeId}' updated.";
            Refresh();
        }

        private void AddStartConditionFromHelper()
        {
            var list = ParseJsonList(_questStartConditionsJsonInput.text, new List<QuestCondition>());
            var type = ParseEnum(DropdownText(_startCondTypeDropdown, QuestConditionType.None.ToString()), QuestConditionType.None);
            int intValue = ParseInt(_startCondIntInput.text, 0);
            list.Add(new QuestCondition { Type = type, IntValue = intValue });
            _questStartConditionsJsonInput.text = SerializeJson(list);
        }

        private void AddNodeConditionFromHelper()
        {
            var list = ParseJsonList(_nodeConditionsJsonInput.text, new List<QuestCondition>());
            var type = ParseEnum(DropdownText(_nodeCondTypeDropdown, QuestConditionType.None.ToString()), QuestConditionType.None);
            int intValue = ParseInt(_nodeCondIntInput.text, 0);
            list.Add(new QuestCondition { Type = type, IntValue = intValue });
            _nodeConditionsJsonInput.text = SerializeJson(list);
        }

        private void AddNodeRewardFromHelper()
        {
            var list = ParseJsonList(_nodeRewardsJsonInput.text, new List<QuestReward>());
            var type = ParseEnum(DropdownText(_nodeRewardTypeDropdown, QuestRewardType.None.ToString()), QuestRewardType.None);
            int targetId = ParseInt(_nodeRewardTargetInput.text, 0);
            int amount = Mathf.Max(1, ParseInt(_nodeRewardAmountInput.text, 1));
            list.Add(new QuestReward { Type = type, TargetId = targetId, Amount = amount });
            _nodeRewardsJsonInput.text = SerializeJson(list);
        }

        private void AddNodeRewardRuleFromHelper()
        {
            var rules = ParseJsonList(_nodeRewardRulesJsonInput.text, new List<QuestRewardRule>());
            var mode = ParseEnum(DropdownText(_nodeRuleModeDropdown, QuestConditionMode.All.ToString()), QuestConditionMode.All);
            var conditionType = ParseEnum(DropdownText(_nodeRuleCondTypeDropdown, QuestConditionType.None.ToString()), QuestConditionType.None);
            int conditionInt = ParseInt(_nodeRuleCondIntInput.text, 0);
            var rewardType = ParseEnum(DropdownText(_nodeRuleRewardTypeDropdown, QuestRewardType.None.ToString()), QuestRewardType.None);
            int rewardTarget = ParseInt(_nodeRuleRewardTargetInput.text, 0);
            int rewardAmount = Mathf.Max(1, ParseInt(_nodeRuleRewardAmountInput.text, 1));

            var rule = new QuestRewardRule
            {
                ConditionMode = mode,
                Conditions = new List<QuestCondition>
                {
                    new QuestCondition
                    {
                        Type = conditionType,
                        IntValue = conditionInt,
                        IntValues = new List<int>(),
                        StringValue = string.Empty,
                        StringValues = new List<string>()
                    }
                },
                Rewards = new List<QuestReward>
                {
                    new QuestReward
                    {
                        Type = rewardType,
                        TargetId = rewardTarget,
                        Amount = rewardAmount
                    }
                }
            };
            rules.Add(rule);
            _nodeRewardRulesJsonInput.text = SerializeJson(rules);
        }

        private void Save()
        {
            _context.SaveQuests(out var msg);
            _validationText.text = msg;
        }

        private void Reload()
        {
            _validationText.text = _context.ReloadQuests();
            _selectedQuestId = _context.Quests.FirstOrDefault()?.QuestId;
            _selectedNodeId = null;
            SetEditorMode(EditorMode.None);
            Refresh();
        }

        private void Validate()
        {
            var validation = _context.ValidateQuests();
            var sb = new StringBuilder();
            sb.Append(QuestAuthoringService.BuildValidationSummary(validation));
            if (validation.Errors.Count > 0)
                sb.Append(" First error: ").Append(validation.Errors[0]);
            else if (validation.Warnings.Count > 0)
                sb.Append(" First warning: ").Append(validation.Warnings[0]);
            _validationText.text = sb.ToString();
        }

        private void StartQuest()
        {
            var quest = GetSelectedQuest();
            if (quest == null)
            {
                _validationText.text = "Select a quest first.";
                return;
            }

            _context.StartQuest(quest.QuestId);
        }

        private static string Safe(string value, string fallback)
        {
            string trimmed = (value ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
        }

        private static int ParseInt(string value, int fallback)
        {
            return int.TryParse((value ?? string.Empty).Trim(), out int parsed) ? parsed : fallback;
        }

        private static List<string> ParseCsv(string value)
        {
            return (value ?? string.Empty)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<int> ParseIntCsv(string value)
        {
            var result = new List<int>();
            var seen = new HashSet<int>();
            var parts = (value ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i].Trim(), out int parsed))
                    continue;
                if (parsed <= 0 || !seen.Add(parsed))
                    continue;
                result.Add(parsed);
            }

            return result;
        }

        private static TEnum ParseEnum<TEnum>(string value, TEnum fallback) where TEnum : struct
        {
            if (Enum.TryParse(value ?? string.Empty, true, out TEnum parsed))
                return parsed;
            return fallback;
        }

        private static string SerializeJson<T>(List<T> value)
        {
            try
            {
                return JsonConvert.SerializeObject(value ?? new List<T>());
            }
            catch
            {
                return "[]";
            }
        }

        private static List<T> ParseJsonList<T>(string value, List<T> fallback)
        {
            string raw = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw))
                return fallback ?? new List<T>();

            try
            {
                var parsed = JsonConvert.DeserializeObject<List<T>>(raw);
                return parsed ?? (fallback ?? new List<T>());
            }
            catch
            {
                return fallback ?? new List<T>();
            }
        }
    }
}


