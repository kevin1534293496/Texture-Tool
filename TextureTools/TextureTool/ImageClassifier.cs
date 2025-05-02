using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static class ImageClassifier
{   
    /// <summary>
    /// 按分辨率分类
    /// </summary>
    public static Dictionary<string, List<Texture2D>> ClassifyByResolution(List<Texture2D> images)
    {
        Dictionary<string, List<Texture2D>> resolutionGroups = new Dictionary<string, List<Texture2D>>();

        foreach (var image in images)
        {
            string resolution = $"{image.width}x{image.height}";
            if (!resolutionGroups.ContainsKey(resolution))
            {
                resolutionGroups[resolution] = new List<Texture2D>();
            }
            resolutionGroups[resolution].Add(image);
        }

        return resolutionGroups;
    }

    /// <summary>
    /// 按文件格式分类（如.png、.jpg、.dds）
    /// 注意：DDS文件需特殊处理
    /// </summary>
    public static Dictionary<string, List<Texture2D>> ClassifyByFormat(List<Texture2D> images)
    {
        Dictionary<string, List<Texture2D>> formatGroups = new Dictionary<string, List<Texture2D>>();
    
        foreach (var image in images)
        {
            // 获取图片的路径
            string assetPath = AssetDatabase.GetAssetPath(image);
            string format = Path.GetExtension(assetPath)?.ToLower(); // 从路径中获取扩展名
            
            // 特殊处理无扩展名的DDS文件
            // 如果路径为空或没有扩展名，检查是否是 .dds 文件
            if (string.IsNullOrEmpty(format))
            {
                if (image.name.EndsWith(".dds", System.StringComparison.OrdinalIgnoreCase))
                {
                    format = ".dds";
                }
                else
                {
                    format = "Unknown"; // 如果仍无法识别，标记为 "Unknown"
                }
            }
    
            if (!formatGroups.ContainsKey(format))
            {
                formatGroups[format] = new List<Texture2D>();
            }
            formatGroups[format].Add(image);
        }
    
        return formatGroups;
    }

    /// <summary>
    /// 按文件大小分类
    /// </summary>
    public static Dictionary<string, List<Texture2D>> ClassifyByFileSize(List<Texture2D> images, string folderPath)
    {
        Dictionary<string, List<Texture2D>> sizeGroups = new Dictionary<string, List<Texture2D>>();

        foreach (var image in images)
        {
            string assetPath = AssetDatabase.GetAssetPath(image);
            string fullPath = Path.Combine(folderPath, assetPath.Replace("Assets", "").TrimStart('/'));
            FileInfo fileInfo = new FileInfo(fullPath);

            string sizeCategory = fileInfo.Length > 1024 * 1024 ? "Large" : "Small"; // 大于1MB为Large，否则为Small
            if (!sizeGroups.ContainsKey(sizeCategory))
            {
                sizeGroups[sizeCategory] = new List<Texture2D>();
            }
            sizeGroups[sizeCategory].Add(image);
        }

        return sizeGroups;
    }
    
    /// <summary>
    /// 按主色调分类
    /// 通过计算像素平均RGB值判断颜色倾向
    /// </summary>
    public static Dictionary<string, List<Texture2D>> ClassifyByColor(List<Texture2D> images)
    {
        Dictionary<string, List<Texture2D>> colorGroups = new Dictionary<string, List<Texture2D>>
        {
            { "Red", new List<Texture2D>() },
            { "Green", new List<Texture2D>() },
            { "Blue", new List<Texture2D>() }
        };

        foreach (var image in images)
        {
            Vector3 dominantColor = GetDominantColor(image);

            // 判断RGB通道的最大值，确定颜色侧重
            if (dominantColor.x > dominantColor.y && dominantColor.x > dominantColor.z)
            {
                colorGroups["Red"].Add(image);
            }
            else if (dominantColor.y > dominantColor.x && dominantColor.y > dominantColor.z)
            {
                colorGroups["Green"].Add(image);
            }
            else
            {
                colorGroups["Blue"].Add(image);
            }
        }

        return colorGroups;
    }

    /// <summary>
    /// 分析图像真实度（通过饱和度和对比度）
    /// </summary>
    public static Dictionary<string, List<Texture2D>> ClassifyByRealism(List<Texture2D> images)
    {
        Dictionary<string, List<Texture2D>> realismGroups = new Dictionary<string, List<Texture2D>>
        {
            { "Realistic", new List<Texture2D>() },
            { "Cartoon", new List<Texture2D>() }
        };

        foreach (var image in images)
        {
            if (!image.isReadable)
            {
                Debug.LogWarning($"Texture {image.name} is not readable. Skipping.");
                continue;
            }

            // 计算图片的平均饱和度和对比度
            (float averageSaturation, float contrast) = AnalyzeImage(image);

            // 根据饱和度和对比度分类
            if (averageSaturation < 0.4f && contrast > 0.5f)
            {
                realismGroups["Realistic"].Add(image);
            }
            else
            {
                realismGroups["Cartoon"].Add(image);
            }
        }

        return realismGroups;
    }

    /// <summary>
    /// 真实度匹配算法
    /// 低饱和度 + 高对比度 -> 真实风格；否则 -> 卡通风格
    //  关键点：计算像素的平均饱和度和亮度标准差
    /// <summary>
    public static (float averageSaturation, float contrast) AnalyzeImage(Texture2D image)
    {
        Color[] pixels = image.GetPixels();
        float totalSaturation = 0f;
        float totalLuminance = 0f;
        float luminanceSquaredSum = 0f;

        foreach (var pixel in pixels)
        {
            // 转换为 HSV 获取饱和度
            Color.RGBToHSV(pixel, out _, out float saturation, out float value);
            totalSaturation += saturation;

            // 计算亮度（对比度相关）
            float luminance = 0.2126f * pixel.r + 0.7152f * pixel.g + 0.0722f * pixel.b;
            totalLuminance += luminance;
            luminanceSquaredSum += luminance * luminance;
        }

        int pixelCount = pixels.Length;
        float averageSaturation = totalSaturation / pixelCount;

        // 计算对比度（亮度的标准差）
        float meanLuminance = totalLuminance / pixelCount;
        float contrast = Mathf.Sqrt((luminanceSquaredSum / pixelCount) - (meanLuminance * meanLuminance));

        // 关键点：计算像素的平均饱和度和亮度标准差
        return (averageSaturation, contrast);
    }

    /// <summary>
    /// 颜色匹配算法
    /// 期望颜色RGB值与实际颜色RGB值的distance
    /// 关键点：计算像素的平均RGB值（色彩变化较大或噪声较大会影响最终结果）
    /// </summary>
    public static Vector3 GetDominantColor(Texture2D image)
    {
        if (!image.isReadable)
        {
            Debug.LogWarning($"Texture {image.name} is not readable. Skipping.");
            return Vector3.zero; // 返回默认值
        }

        // 优化采样率以提升性能
        int step = Mathf.Max(image.width, image.height) / 10 + 1;
        Color[] pixels = image.GetPixels(0, 0, image.width, image.height);

        float r = 0, g = 0, b = 0;
        int sampledCount = 0;

        for (int i = 0; i < pixels.Length; i += step)
        {
            r += pixels[i].r;
            g += pixels[i].g;
            b += pixels[i].b;
            sampledCount++;
        }

        return sampledCount == 0 ? Vector3.zero : new Vector3(r / sampledCount, g / sampledCount, b / sampledCount);
    }

    /// <summary>
    /// 判断颜色是否相似
    /// 通过计算颜色之间的距离来判断是否show这张图片
    /// </summary>
    public static bool IsColorSimilar(Vector3 color1, Color targetColor, float threshold)
    {
        Vector3 color2 = new Vector3(targetColor.r, targetColor.g, targetColor.b);
        float distance = Vector3.Distance(color1, color2);
        return distance <= threshold;
    }

    public static bool IsRealismInRange((float averageSaturation, float contrast) realismData, float minThreshold, float maxThreshold)
    {
        // 计算真实度得分
        float realismScore = (1.0f - realismData.averageSaturation) * realismData.contrast;

        // 判断真实度是否在范围内
        return realismScore >= minThreshold && realismScore <= maxThreshold;
    }

    /// <summary>
    /// 加载DDS纹理（跳过128字节文件头）
    /// </summary>
    public static Texture2D LoadDDSTexture(string filePath)
    {
        if (!File.Exists(filePath)) return null;

        byte[] ddsBytes = File.ReadAllBytes(filePath);

        // 简略检查
        if (ddsBytes[4] != 124) // not DDS
            return null;

        int height = ddsBytes[13] * 256 + ddsBytes[12];
        int width = ddsBytes[17] * 256 + ddsBytes[16];
        int mipMapCount = ddsBytes[28];

        TextureFormat format = TextureFormat.DXT1;
        if (ddsBytes[87] == 0x35) format = TextureFormat.DXT5;

        // 关键点：读取DDS文件头信息，创建Texture2D并加载数据
        Texture2D texture = new Texture2D(width, height, format, mipMapCount > 1)
        {
            name = Path.GetFileName(filePath)
        };

        texture.LoadRawTextureData(ddsBytes.Skip(128).ToArray()); // 跳过 DDS 文件头部的 128 字节
        texture.Apply();

        // 设置纹理名称，包含 .dds 扩展名
        texture.name = Path.GetFileName(filePath);

        return texture;
    }

    // 添加其他分类方法，如噪声程度和图片来源
    public static string GetResolution(Texture2D image)
    {
        return $"{image.width}x{image.height}";
    }
}