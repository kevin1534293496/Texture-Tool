using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class MainWindow : EditorWindow
{   
    // ---- UI相关字段 ----
    private string folderPath = "Assets/Image/Rename";
    private string newFileNamePrefix = "";      // 新文件名前缀
    private List<Texture2D> allImages = new List<Texture2D>();
    private List<Texture2D> selectedImages = new List<Texture2D>();
    private Vector2 scrollPosition = Vector2.zero; 
    private Vector2 filterScrollPosition = Vector2.zero;
    
    private bool showRenameInput = false; // 控制文件名前缀输入框的显示
    private bool showFilters = true;     // 控制筛选条件的展开与收起
    private bool isAllSelected = false; // 用于跟踪是否已全选
    private bool enableReorder = false; // 是否启用重排序
    
    private GUIStyle titleStyle; 
    private GUIStyle subtitleStyle;
    private GUIStyle labelStyle;

    // ---- 分辨率筛选 ----
    private HashSet<string> selectedResolutions = new HashSet<string>();

    // ---- 格式筛选 ----
    private HashSet<string> selectedFormats = new HashSet<string>();  
     
    // ---- 颜色筛选 ----
    private Color selectedColor = Color.white;
    private float colorThreshold = 0.2f;
    private bool useColorFilter = false;
    private Dictionary<Texture2D, Vector3> dominantColorCache = new Dictionary<Texture2D, Vector3>();

    // ---- 真实度筛选 ----
    private HashSet<string> selectedRealism = new HashSet<string>();
    private Dictionary<string, List<Texture2D>> realismGroups = new Dictionary<string, List<Texture2D>>();
    private float realismMinThreshold = 0.0f; // 真实度最小阈值
    private float realismMaxThreshold = 1.0f; // 真实度最大阈值
    private bool useRealismFilter = false;    // 是否启用真实度筛选
    private Dictionary<Texture2D, (float averageSaturation, float contrast)> realismCache = new Dictionary<Texture2D, (float averageSaturation, float contrast)>();

    // ---- 筛选预处理 ----
    private Dictionary<string, List<Texture2D>> resolutionGroups = new Dictionary<string, List<Texture2D>>();
    private Dictionary<string, List<Texture2D>> formatGroups = new Dictionary<string, List<Texture2D>>();
    private Dictionary<Texture2D, string> texturePaths = new Dictionary<Texture2D, string>();

    [MenuItem("Tools/Image Manager")]
    public static void ShowWindow()
    {
        MainWindow window = GetWindow<MainWindow>("Image Manager");
        window.Show();
    }

    /// <summary>
    /// 主界面渲染方法
    /// </summary>
    private void OnGUI()
    {
        // 初始化样式
        InitializeStyles(); 

        // 标题
        GUILayout.Space(10);
        GUILayout.Label("贴图相似性检测工具", titleStyle);
        GUILayout.Space(10);
        
        // 信息
        GUILayout.Space(5);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Author: YangGuotao", titleStyle);
        GUILayout.Label("Version: 3.0", titleStyle);
        GUILayout.Label("Date: 2025-04-06", titleStyle);
        GUILayout.EndHorizontal();
        GUILayout.Space(5);

        // 文件夹选择按钮
        if (GUILayout.Button("选择文件夹"))
        {
            string selectedFolder = EditorUtility.OpenFolderPanel("选择文件夹", "Assets", "");
            if (!string.IsNullOrEmpty(selectedFolder))
            {
                folderPath = selectedFolder.Replace(Application.dataPath, "Assets");
                LoadImagesFromFolder(folderPath);
            }
        }

        GUILayout.Space(5);
        GUILayout.Label($"当前文件夹: {folderPath}", labelStyle);

        if (allImages.Count > 0)
        {
            GUILayout.Space(10);
            GUILayout.Label($"共 {allImages.Count} 张图片", labelStyle);

            // 筛选条件（可折叠）
            GUILayout.Space(10);
            GUILayout.Label("筛选条件", subtitleStyle);
            GUILayout.Space(5);

            showFilters = EditorGUILayout.Foldout(showFilters, showFilters ? "收起" : "展开");
            if (showFilters)
            {
                GUILayout.Space(5);

                // 分辨率筛选
                GUILayout.Label("分辨率", subtitleStyle);
                RenderToggleGroup(resolutionGroups.Keys, selectedResolutions);

                // 格式筛选
                GUILayout.Space(10);
                GUILayout.Label("格式", subtitleStyle);
                RenderToggleGroup(formatGroups.Keys, selectedFormats);

                // 颜色筛选
                GUILayout.Space(10);
                GUILayout.Label("颜色筛选", subtitleStyle);
                useColorFilter = EditorGUILayout.Toggle("启用颜色筛选", useColorFilter);
                if (useColorFilter)
                {
                    GUILayout.Space(5);
                    selectedColor = EditorGUILayout.ColorField("目标颜色", selectedColor);
                    colorThreshold = EditorGUILayout.Slider("颜色阈值", colorThreshold, 0f, 1f);
                }

                // 真实度筛选
                GUILayout.Space(10);
                GUILayout.Label("真实度筛选", subtitleStyle);
                useRealismFilter = EditorGUILayout.Toggle("启用真实度筛选", useRealismFilter);
                if (useRealismFilter)
                {
                    GUILayout.Space(5);
                    GUILayout.Label($"真实度范围: {realismMinThreshold:F2} - {realismMaxThreshold:F2}", labelStyle);
                    EditorGUILayout.MinMaxSlider(ref realismMinThreshold, ref realismMaxThreshold, 0.0f, 1.0f);
                }
            }

            GUILayout.Space(10);

            // 全选按钮
            if (GUILayout.Button("全选", GUILayout.Height(30)))
            {
                SelectAllImages();
            }

            // 图片展示区域（网格布局）
            GUILayout.Space(10);
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(300));

            int imagesPerRow = 5;
            int currentCount = 0;
 
            var filteredImages = GetFilteredImages(); // 获取筛选后的图片

            GUILayout.BeginVertical();
            while (currentCount < filteredImages.Count)
            {
                GUILayout.BeginHorizontal();

                for (int i = 0; i < imagesPerRow && currentCount < filteredImages.Count; i++, currentCount++)
                {
                    var image = filteredImages[currentCount];
                    GUILayout.BeginVertical(GUILayout.Width(100));

                    bool isSelected = selectedImages.Contains(image);
                    bool newIsSelected = EditorGUILayout.ToggleLeft(image.name, isSelected, GUILayout.Width(100));
                    if (newIsSelected != isSelected)
                    {
                        if (newIsSelected)
                            selectedImages.Add(image);
                        else
                            selectedImages.Remove(image);
                    }

                    GUILayout.Box(image, GUILayout.Width(100), GUILayout.Height(100));
                    GUILayout.EndVertical();
                }

                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();

            GUILayout.EndScrollView();

            GUILayout.Space(10);

            // 批量处理按钮（删除/移动/重命名）
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("批量删除", GUILayout.Height(30)))
            {
                if (selectedImages.Count > 0)
                {
                    BatchProcessor.DeleteImages(selectedImages);
                    Debug.Log($"已删除 {selectedImages.Count} 张图片");
                    selectedImages.Clear(); // 清空已选列表
                }
                else
                {
                    Debug.LogWarning("未选择任何图片进行删除！");
                }
            }

            if (GUILayout.Button("批量移动", GUILayout.Height(30)))
            {
                if (selectedImages.Count > 0)
                {
                    string targetFolder = EditorUtility.OpenFolderPanel("选择目标文件夹", "Assets", "");
                    if (!string.IsNullOrEmpty(targetFolder))
                    {
                        BatchProcessor.MoveImages(selectedImages, targetFolder);
                        Debug.Log($"已移动 {selectedImages.Count} 张图片到 {targetFolder}");
                        selectedImages.Clear(); // 清空已选列表
                    }
                }
                else
                {
                    Debug.LogWarning("未选择任何图片进行移动！");
                }
            }
            
            if (GUILayout.Button("批量重命名", GUILayout.Height(30)))
            {
                if (selectedImages.Count > 0)
                {
                    showRenameInput = true; // 显示文件名前缀输入框
                }
                else
                {
                    Debug.LogWarning("未选择任何图片进行重命名！");
                }
            }

            GUILayout.EndHorizontal();
            
            // 如果点击了“批量重命名”，显示输入框
            if (showRenameInput)
            {   
                GUILayout.Space(10);
                GUILayout.BeginVertical();

                GUILayout.Label("输入文件名前缀：", subtitleStyle);
                newFileNamePrefix = EditorGUILayout.TextField("文件名前缀", newFileNamePrefix);

                // 添加“重排序”复选框
                enableReorder = EditorGUILayout.Toggle("重排序", enableReorder);

                GUILayout.Space(5); 
                GUILayout.BeginHorizontal(); // 将确认和取消按钮放在同一行
                
                // 添加确认按钮
                if (GUILayout.Button("确认重命名", GUILayout.Height(30)))
                {
                    if (!string.IsNullOrEmpty(newFileNamePrefix))
                    {
                        BatchProcessor.RenameImages(selectedImages, newFileNamePrefix, texturePaths,enableReorder);
                        Debug.Log($"已批量重命名为前缀：{newFileNamePrefix}");
                        selectedImages.Clear(); // 清空已选列表
                        showRenameInput = false; // 隐藏输入框
                    }
                    else
                    {
                        Debug.LogWarning("文件名前缀不能为空！");
                    }
                }
            
                // 添加取消按钮
                if (GUILayout.Button("取消", GUILayout.Height(30)))
                {
                    showRenameInput = false; // 隐藏输入框
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndHorizontal();
        }
        else
        {   
            GUILayout.Space(10);
            GUILayout.Label("当前文件夹中没有图片。", labelStyle);
        }
    }

    private void InitializeStyles()
    {
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter
            };

            subtitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleLeft
            };

            labelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft
            };
        }
    }

    private void SelectAllImages()
    {
        var filteredImages = GetFilteredImages();

        if (isAllSelected)
        {
            // 取消全选
            foreach (var image in filteredImages)
            {
                selectedImages.Remove(image);
            }
            Debug.Log("已取消全选");
        }
        else
        {
            // 全选
            foreach (var image in filteredImages)
            {
                if (!selectedImages.Contains(image))
                {
                    selectedImages.Add(image);
                }
            }
            Debug.Log($"已全选 {filteredImages.Count} 张图片");
        }

        // 切换全选状态
        isAllSelected = !isAllSelected;
    }

    /// <summary>
    /// 加载指定文件夹下的所有图片（支持多种格式）
    /// </summary>
    private void LoadImagesFromFolder(string path)
    {
        allImages.Clear();
        dominantColorCache.Clear(); // 清空颜色缓存
        realismCache.Clear();       // 清空真实度缓存
        texturePaths.Clear();       // 清空纹理路径缓存

        // 获取所有支持的图片文件路径
        string[] filePaths = Directory.GetFiles(path, "*.png")
            .Concat(Directory.GetFiles(path, "*.jpg"))
            .Concat(Directory.GetFiles(path, "*.tga"))
            .Concat(Directory.GetFiles(path, "*.dds"))
            .Concat(Directory.GetFiles(path, "*.tiff"))
            .ToArray();

        foreach (var filePath in filePaths)
        {   
            string assetPath = filePath.Replace(Application.dataPath, "Assets").Replace("\\", "/");
            string extension = Path.GetExtension(assetPath).ToLower();

            // 特殊处理DDS文件
            if (extension == ".dds")
            {
                // 使用 DDSReader 加载 .dds 文件
                Texture2D ddsTexture = ImageClassifier.LoadDDSTexture(filePath);
                if (ddsTexture != null)
                {   
                    allImages.Add(ddsTexture);

                    // 缓存纹理路径
                    texturePaths[ddsTexture] = filePath;

                    // 缓存主色调
                    Vector3 dominantColor = ImageClassifier.GetDominantColor(ddsTexture);
                    dominantColorCache[ddsTexture] = dominantColor;

                    // 缓存真实度数据
                    var realismData = ImageClassifier.AnalyzeImage(ddsTexture);
                    realismCache[ddsTexture] = realismData;
                }
                continue;
            }
            
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);

            // 加载普通纹理
            if (texture != null)
            {
                // 确保纹理可读
                EnableTextureReadable(assetPath);

                allImages.Add(texture);

                // 缓存纹理路径
                texturePaths[texture] = assetPath;

                // 缓存主色调
                Vector3 dominantColor = ImageClassifier.GetDominantColor(texture);
                dominantColorCache[texture] = dominantColor;

                // 缓存真实度数据
                var realismData = ImageClassifier.AnalyzeImage(texture);
                realismCache[texture] = realismData;
            }
        }

        // 分类数据初始化
        resolutionGroups = ImageClassifier.ClassifyByResolution(allImages);
        formatGroups = ImageClassifier.ClassifyByFormat(allImages);
        realismGroups = ImageClassifier.ClassifyByRealism(allImages);

        Debug.Log("图片加载完成！");
    }

    // 启用纹理的 isReadable 属性
    private void EnableTextureReadable(string assetPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer != null && !importer.isReadable)
        {
            importer.isReadable = true;
            importer.textureType = TextureImporterType.Default;
            importer.SaveAndReimport();
        }
    }

    /// <summary>
    /// 获取筛选后的图片列表（根据分辨率、格式、颜色等条件）
    /// </summary>
    private List<Texture2D> GetFilteredImages()
    {
        return allImages.Where(image =>
        {
            // 分辨率筛选
            bool matchesResolution = selectedResolutions.Count == 0 || selectedResolutions.Contains(ImageClassifier.GetResolution(image));

            // 格式筛选
            string path = texturePaths.ContainsKey(image) ? texturePaths[image] : AssetDatabase.GetAssetPath(image);
            string format = !string.IsNullOrEmpty(path) ? Path.GetExtension(path).ToLower() : "Unknown";
            bool matchesFormat = selectedFormats.Count == 0 || selectedFormats.Contains(format);

            // 真实度筛选
            bool matchesRealism = !useRealismFilter || (realismCache.ContainsKey(image) &&
                ImageClassifier.IsRealismInRange(realismCache[image], realismMinThreshold, realismMaxThreshold));

            // 颜色筛选
            bool matchesColor = !useColorFilter || (dominantColorCache.ContainsKey(image) &&
                ImageClassifier.IsColorSimilar(dominantColorCache[image], selectedColor, colorThreshold));

            return matchesResolution && matchesFormat && matchesRealism && matchesColor;
        }).ToList();
    }

    /// <summary>
    /// 渲染复选框组（用于分辨率、格式等筛选）
    /// </summary>
    private void RenderToggleGroup(IEnumerable<string> options, HashSet<string> selectedOptions, int togglesPerRow = 5)
    {
        int count = 0;
        GUILayout.BeginHorizontal();
        foreach (var option in options)
        {
            bool isSelected = selectedOptions.Contains(option);
            bool newIsSelected = GUILayout.Toggle(isSelected, option, GUILayout.Width(100));
            if (newIsSelected != isSelected)
            {
                if (newIsSelected) selectedOptions.Add(option);
                else selectedOptions.Remove(option);
            }

            count++;
            if (count % togglesPerRow == 0)
            {
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
            }
        }
        GUILayout.EndHorizontal();
    }
}