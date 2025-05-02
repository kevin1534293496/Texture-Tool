using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;

public static class BatchProcessor
{
    /// <summary>
    /// 删除选定的图片资源。
    /// </summary>
    /// <param name="images">要删除的图片资源列表。</param>
    public static void DeleteImages(List<Texture2D> images)
    {
        List<string> failedPaths = new List<string>();

        foreach (var image in images)
        {
            string path = AssetDatabase.GetAssetPath(image);
            // 尝试删除资源，失败时记录路径
            if (!AssetDatabase.DeleteAsset(path))
            {
                failedPaths.Add(path);
                Debug.LogError($"删除失败: {path}");
            }
            else
            {
                Debug.Log($"成功删除: {path}");
            }
        }

        // 输出失败结果
        if (failedPaths.Count > 0)
        {
            Debug.LogError("以下资源删除失败:");
            foreach (var path in failedPaths)
            {
                Debug.LogError(path);
            }
        }
        else
        {
            Debug.Log("所有选定的图片已成功删除。");
        }

        AssetDatabase.SaveAssets();  // 确保资源变动保存
    } 

    /// <summary>
    /// 移动图片到指定文件夹（注意：直接使用文件系统API，需确保目标路径有效）
    /// </summary>
    /// <param name="images">待移动的图片列表</param>
    /// <param name="newFolder">目标文件夹路径</param>
    public static void MoveImages(List<Texture2D> images, string newFolder)
    {
        List<string> failedMoves = new List<string>();

        foreach (var image in images)
        {
            string oldPath = AssetDatabase.GetAssetPath(image);
            string fileName = Path.GetFileName(oldPath);
            string newPath = Path.Combine(newFolder, fileName).Replace("\\", "/");

            try
            {   
                // 使用文件系统API直接移动文件
                File.Move(oldPath, newPath);
                Debug.Log($"成功移动: {oldPath} -> {newPath}");
            }
            catch (IOException ex)
            {
                failedMoves.Add(oldPath);
                Debug.LogError($"移动失败: {oldPath} -> {newPath}, 错误: {ex.Message}");
            }
        }

        // 输出移动结果
        if (failedMoves.Count > 0)
        {
            Debug.LogError("以下资源移动失败:");
            foreach (var path in failedMoves)
            {
                Debug.LogError(path);
            }
        }
        else
        {
            Debug.Log("所有选定的图片已成功移动。");
        }
    }

    /// <summary>
    /// 批量重命名选定的图片资源。
    /// </summary>
    /// <param name="images">要重命名的图片资源列表。</param>
    /// <param name="newName">新的文件名（不包含扩展名）。</param>
    public static void RenameImages(List<Texture2D> images, string newName, Dictionary<Texture2D, string> texturePaths, bool enableReorder)
    {
        int index = 1;
        List<string> failedRenames = new List<string>();
    
        foreach (var image in images)
        {
            // 优先从 texturePaths 获取路径
            string oldPath = texturePaths.ContainsKey(image) ? texturePaths[image] : AssetDatabase.GetAssetPath(image);
            if (string.IsNullOrEmpty(oldPath))
            {
                Debug.LogError($"无法获取文件路径: {image.name}");
                failedRenames.Add(image.name);
                continue;
            }
    
            string directory = Path.GetDirectoryName(oldPath);
            string extension = Path.GetExtension(oldPath);
    
            // 根据是否启用重排序，设置文件名后缀格式
            string suffix = enableReorder ? index.ToString("D3") : index.ToString();
            string newFileName = $"{newName}_{suffix}{extension}";
            string newPath = Path.Combine(directory, newFileName).Replace("\\", "/");
    
            // 检查是否为 .dds 文件
            if (extension.ToLower() == ".dds")
            {
                try
                {
                    // 使用文件系统操作重命名 .dds 文件
                    File.Move(oldPath, newPath);
                    Debug.Log($"成功重命名: {oldPath} -> {newPath}");
                }
                catch (IOException ex)
                {
                    failedRenames.Add(oldPath);
                    Debug.LogError($"重命名失败: {oldPath} -> {newPath}, 错误: {ex.Message}");
                }
            }
            else
            {
                // 调用 Unity API 重命名其他格式的文件
                if (AssetDatabase.RenameAsset(oldPath, newFileName) != "")
                {
                    failedRenames.Add(oldPath);
                    Debug.LogError($"重命名失败: {oldPath} -> {newPath}");
                }
                else
                {
                    Debug.Log($"成功重命名: {oldPath} -> {newPath}");
                }
            }
    
            index++;
        }
    
        // 输出重命名结果
        if (failedRenames.Count > 0)
        {
            Debug.LogError("以下资源重命名失败:");
            foreach (var path in failedRenames)
            {
                Debug.LogError(path);
            }
        }
        else
        {
            Debug.Log("所有选定的图片已成功重命名。");
        }
    
        AssetDatabase.Refresh(); // 刷新资源数据库
    }
}