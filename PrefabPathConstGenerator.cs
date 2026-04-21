using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace Tools.Editor
{
    /// <summary>
    /// 预制体路径常量类生成器
    /// 扫描指定文件夹中的预制体，生成包含路径常量的C#脚本
    /// </summary>
    public class PrefabPathConstGenerator : EditorWindow
    {
        private const string Title = "预制体路径常量生成器";

        // 配置文件路径：运行时根据脚本位置动态计算
        private string PrefsFilePath
        {
            get
            {
                if (_prefsFilePath != null) return _prefsFilePath;
                string[] guids = AssetDatabase.FindAssets("t:MonoScript PrefabPathConstGenerator");
                if (guids.Length > 0)
                {
                    string scriptPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                    string scriptDir = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(
                        Directory.GetParent(Application.dataPath).FullName, scriptPath)));
                    _prefsFilePath = Path.Combine(scriptDir, "PrefabPathConstGeneratorPrefs.json");
                }
                else
                {
                    _prefsFilePath = Path.Combine(Application.dataPath, "PrefabPathConstGeneratorPrefs.json");
                }
                return _prefsFilePath;
            }
        }
        private string _prefsFilePath;

        // 目标文件夹（要扫描的预制体所在文件夹）
        private DefaultAsset _targetFolder;
        // 输出文件夹（生成的脚本存放位置）
        private DefaultAsset _outputFolder;
        // 生成的类名
        private string _className = "PrefabPath";
        // 是否包含子文件夹
        private bool _includeSubFolders = true;
        // 路径格式：true=完整路径, false=仅名称
        private bool _useFullPath = true;

        // 扫描到的预制体信息列表
        private List<PrefabEntry> _prefabEntries = new List<PrefabEntry>();
        // 是否已扫描
        private bool _hasScanned;
        // 全选/全不选
        private bool _selectAll = true;
        // 滚动位置
        private Vector2 _scrollPos;

        /// <summary>
        /// 预制体条目，记录路径和选中状态
        /// </summary>
        private class PrefabEntry
        {
            public string AssetPath;   // Assets/xx/yy.prefab 形式的完整路径
            public string RelativePath; // 相对于扫描根目录的路径
            public string Name;        // 不含扩展名的文件名
            public bool Selected = true;
        }

        [MenuItem("Tools/预制体路径常量生成器")]
        public static void ShowWindow()
        {
            var window = GetWindow<PrefabPathConstGenerator>(Title);
            window.minSize = new Vector2(450, 500);
        }

        private void OnEnable()
        {
            LoadPrefs();
        }

        private void OnDisable()
        {
            SavePrefs();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(Title, EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            DrawFolderSelector();
            DrawOptions();
            DrawScanButton();
            DrawPrefabList();
            DrawGenerateButton();
        }

        #region UI绘制

        private void DrawFolderSelector()
        {
            EditorGUILayout.LabelField("文件夹设置", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _targetFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                "目标文件夹", _targetFolder, typeof(DefaultAsset), false);
            _outputFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                "输出文件夹", _outputFolder, typeof(DefaultAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                _hasScanned = false;
            }

            EditorGUILayout.Space(4);
        }

        private void DrawOptions()
        {
            EditorGUILayout.LabelField("生成选项", EditorStyles.boldLabel);

            _className = EditorGUILayout.TextField("类名", _className);
            _includeSubFolders = EditorGUILayout.Toggle("包含子文件夹", _includeSubFolders);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("路径格式", EditorStyles.miniBoldLabel);
            int pathFmtIdx = _useFullPath ? 0 : 1;
            pathFmtIdx = GUILayout.SelectionGrid(pathFmtIdx,
                new[] { "完整相对路径 (如: Prefabs/UI/MainPanel)", "仅预制体名称 (如: MainPanel)" }, 1);
            _useFullPath = pathFmtIdx == 0;

            EditorGUILayout.Space(4);
        }

        private void DrawScanButton()
        {
            if (GUILayout.Button("扫描预制体", GUILayout.Height(30)))
            {
                ScanPrefabs();
            }

            if (_targetFolder == null)
            {
                EditorGUILayout.HelpBox("请先指定目标文件夹", MessageType.Warning);
            }

            EditorGUILayout.Space(4);
        }

        private void DrawPrefabList()
        {
            if (!_hasScanned) return;

            EditorGUILayout.LabelField($"预制体列表 ({_prefabEntries.Count} 个)", EditorStyles.boldLabel);

            // 全选/全不选
            EditorGUI.BeginChangeCheck();
            _selectAll = EditorGUILayout.Toggle("全选", _selectAll);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var entry in _prefabEntries)
                    entry.Selected = _selectAll;
            }

            EditorGUILayout.Space(2);

            // 带滚动的列表
            int selectedCount = _prefabEntries.Count(e => e.Selected);
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));
            foreach (var entry in _prefabEntries)
            {
                EditorGUI.BeginChangeCheck();
                entry.Selected = EditorGUILayout.ToggleLeft(
                    _useFullPath ? entry.RelativePath : entry.Name, entry.Selected);
                if (EditorGUI.EndChangeCheck())
                {
                    _selectAll = _prefabEntries.TrueForAll(e => e.Selected);
                }
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.LabelField($"已选择 {selectedCount}/{_prefabEntries.Count} 个",
                EditorStyles.miniLabel);

            EditorGUILayout.Space(4);
        }

        private void DrawGenerateButton()
        {
            if (!_hasScanned) return;

            EditorGUI.BeginDisabledGroup(selectedCount == 0 || _outputFolder == null);
            if (GUILayout.Button("生成常量脚本", GUILayout.Height(35)))
            {
                GenerateScript();
            }
            EditorGUI.EndDisabledGroup();

            if (_outputFolder == null && _hasScanned)
            {
                EditorGUILayout.HelpBox("请指定输出文件夹", MessageType.Warning);
            }
        }

        private int selectedCount => _prefabEntries.Count(e => e.Selected);

        private void LoadPrefs()
        {
            if (!File.Exists(PrefsFilePath)) return;

            try
            {
                var prefs = JsonUtility.FromJson<PrefsData>(File.ReadAllText(PrefsFilePath));
                if (prefs == null) return;

                if (!string.IsNullOrEmpty(prefs.targetFolder))
                    _targetFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(prefs.targetFolder);
                if (!string.IsNullOrEmpty(prefs.outputFolder))
                    _outputFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(prefs.outputFolder);
                if (!string.IsNullOrEmpty(prefs.className))
                    _className = prefs.className;
                _includeSubFolders = prefs.includeSubFolders;
                _useFullPath = prefs.useFullPath;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[PrefabPathConstGenerator] 读取配置失败: {e.Message}");
            }
        }

        private void SavePrefs()
        {
            try
            {
                var prefs = new PrefsData
                {
                    targetFolder = _targetFolder ? AssetDatabase.GetAssetPath(_targetFolder) : "",
                    outputFolder = _outputFolder ? AssetDatabase.GetAssetPath(_outputFolder) : "",
                    className = _className,
                    includeSubFolders = _includeSubFolders,
                    useFullPath = _useFullPath
                };
                File.WriteAllText(PrefsFilePath, JsonUtility.ToJson(prefs, true), Encoding.UTF8);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[PrefabPathConstGenerator] 保存配置失败: {e.Message}");
            }
        }

        [System.Serializable]
        private class PrefsData
        {
            public string targetFolder;
            public string outputFolder;
            public string className = "PrefabPath";
            public bool includeSubFolders = true;
            public bool useFullPath = true;
        }

        #endregion

        #region 扫描逻辑

        private void ScanPrefabs()
        {
            _prefabEntries.Clear();
            _hasScanned = false;

            if (_targetFolder == null)
            {
                EditorUtility.DisplayDialog(Title, "请先指定目标文件夹", "确定");
                return;
            }

            string folderPath = AssetDatabase.GetAssetPath(_targetFolder);
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
            {
                EditorUtility.DisplayDialog(Title, "目标文件夹无效，请重新选择", "确定");
                return;
            }

            string searchOption = _includeSubFolders ? "t:Prefab" : "t:Prefab";
            // 使用 AssetDatabase.FindAssets 获取所有预制体GUID
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath });

            if (!_includeSubFolders)
            {
                // 不包含子文件夹时，过滤掉子目录中的结果
                guids = System.Array.FindAll(guids, g =>
                {
                    string p = AssetDatabase.GUIDToAssetPath(g);
                    return Path.GetDirectoryName(p) == folderPath.Replace('\\', '/');
                });
            }

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!assetPath.EndsWith(".prefab")) continue;

                string name = Path.GetFileNameWithoutExtension(assetPath);
                // 完整相对路径：相对于 Assets/，去掉 .prefab 扩展名
                string relativePath = assetPath;
                if (relativePath.StartsWith("Assets/"))
                    relativePath = relativePath.Substring("Assets/".Length);
                if (relativePath.EndsWith(".prefab"))
                    relativePath = relativePath.Substring(0, relativePath.Length - ".prefab".Length);
                // 去掉 Resources/ 或 Resource/ 前缀（不区分大小写，Resources.Load 不需要该前缀）
                if (relativePath.StartsWith("Resources/", System.StringComparison.OrdinalIgnoreCase))
                {
                    int slashIdx = relativePath.IndexOf('/');
                    relativePath = relativePath.Substring(slashIdx + 1);
                }

                _prefabEntries.Add(new PrefabEntry
                {
                    AssetPath = assetPath,
                    RelativePath = relativePath,
                    Name = name,
                    Selected = true
                });
            }

            _selectAll = true;
            _hasScanned = true;

            if (_prefabEntries.Count == 0)
            {
                EditorUtility.DisplayDialog(Title, $"在 {folderPath} 中未找到任何预制体", "确定");
            }
        }

        #endregion

        #region 脚本生成

        private void GenerateScript()
        {
            // 验证输出文件夹
            string outputFolderPath = AssetDatabase.GetAssetPath(_outputFolder);
            if (string.IsNullOrEmpty(outputFolderPath) || !AssetDatabase.IsValidFolder(outputFolderPath))
            {
                EditorUtility.DisplayDialog(Title, "输出文件夹无效，请重新选择", "确定");
                return;
            }

            if (string.IsNullOrWhiteSpace(_className))
            {
                EditorUtility.DisplayDialog(Title, "请输入类名", "确定");
                return;
            }

            // 过滤选中的条目
            var selected = _prefabEntries.Where(e => e.Selected).ToList();
            if (selected.Count == 0)
            {
                EditorUtility.DisplayDialog(Title, "请至少选择一个预制体", "确定");
                return;
            }

            // 构建C#脚本内容
            string scriptContent = BuildScriptContent(selected);

            // 将 Asset 相对路径转为文件系统绝对路径
            string fullOutputDir = Path.GetFullPath(Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, outputFolderPath));

            if (!Directory.Exists(fullOutputDir))
                Directory.CreateDirectory(fullOutputDir);

            string outputPath = Path.Combine(fullOutputDir, $"{_className}.cs");
            File.WriteAllText(outputPath, scriptContent, Encoding.UTF8);

            AssetDatabase.Refresh();

            Debug.Log($"[PrefabPathConstGenerator] 已生成 {_className}.cs，共 {selected.Count} 个常量。路径: {outputPath}");
            EditorUtility.DisplayDialog(Title,
                $"成功生成 {_className}.cs\n共 {selected.Count} 个常量\n\n路径: {outputPath}",
                "确定");
        }

        /// <summary>
        /// 根据选中的预制体列表构建C#脚本内容
        /// </summary>
        private string BuildScriptContent(List<PrefabEntry> entries)
        {
            var sb = new StringBuilder();

            // 头部注释
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine($"//   由 {Title} 自动生成");
            sb.AppendLine($"//   生成时间: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("//   请勿手动修改此文件，修改将在下次生成时被覆盖");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine();

            sb.AppendLine($"public static class {_className}");
            sb.AppendLine("{");

            // 用于检测常量名冲突，自动添加后缀
            var nameCounts = new Dictionary<string, int>();
            // 第一遍：统计名称出现次数
            foreach (var entry in entries)
            {
                string constName = ToConstName(entry.Name);
                if (nameCounts.ContainsKey(constName))
                    nameCounts[constName]++;
                else
                    nameCounts[constName] = 1;
            }

            // 第二遍：生成常量，有冲突的加后缀
            var usedNames = new Dictionary<string, int>();
            int maxLen = 0;
            // 先计算最长名称用于对齐
            foreach (var entry in entries)
            {
                string constName = GetUniqueConstName(entry.Name, nameCounts, usedNames);
                if (constName.Length > maxLen) maxLen = constName.Length;
            }

            // 重置用于实际生成
            usedNames.Clear();

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                string value = _useFullPath ? entry.RelativePath : entry.Name;
                string constName = GetUniqueConstName(entry.Name, nameCounts, usedNames);

                // 对齐等号
                string padding = new string(' ', maxLen - constName.Length);
                sb.AppendLine($"    public const string {constName} {padding}= \"{value}\";");
            }

            sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>
        /// 将路径/名称转为合法的大写常量名
        /// </summary>
        private static string ToConstName(string pathOrName)
        {
            // 替换分隔符和空格为下划线
            string name = pathOrName.Replace('/', '_').Replace('\\', '_').Replace(' ', '_').Replace('-', '_');
            // 去除连续下划线
            while (name.Contains("__"))
                name = name.Replace("__", "_");
            name = name.Trim('_');
            // 转大写
            name = name.ToUpperInvariant();
            // 确保不以数字开头
            if (name.Length > 0 && char.IsDigit(name[0]))
                name = "_" + name;
            return string.IsNullOrEmpty(name) ? "UNNAMED" : name;
        }

        /// <summary>
        /// 获取唯一的常量名，冲突时添加数字后缀
        /// </summary>
        private static string GetUniqueConstName(string pathOrName,
            Dictionary<string, int> nameCounts, Dictionary<string, int> usedNames)
        {
            string baseName = ToConstName(pathOrName);

            if (!usedNames.ContainsKey(baseName))
            {
                usedNames[baseName] = 1;
                return baseName;
            }

            // 有冲突，加后缀
            int index = usedNames[baseName];
            usedNames[baseName] = index + 1;
            return $"{baseName}_{index}";
        }

        #endregion
    }
}
