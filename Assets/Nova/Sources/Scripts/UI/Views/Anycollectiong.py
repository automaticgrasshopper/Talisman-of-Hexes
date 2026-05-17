import os
import glob

def extract_cs_files():
    # 定义要提取的.cs文件名列表
    target_files = [
        "BaseNavigationPanel.cs",
        "ChoicesNavigationPanel.cs", 
        "GamePadNavigationManager.cs",
        "HelperNavigationController.cs",
        "InputModeImage.cs",
        "LogNavigationController.cs",
        "MainMenuNavigationPanel.cs",
        "NotificationNavigationPanel.cs",
        "SettingsNavigationPanel.cs",
        "TitleSelectionNavigationPanel.cs"
    ]
    
    # 输出文件名
    output_file = "extracted_cs_files.txt"
    
    # 检查当前目录
    current_dir = os.getcwd()
    print(f"当前目录: {current_dir}")
    
    # 获取当前目录下所有的.cs文件
    all_cs_files = glob.glob("*.cs")
    print(f"找到 {len(all_cs_files)} 个.cs文件")
    
    # 创建输出文件
    with open(output_file, 'w', encoding='utf-8') as outfile:
        outfile.write("=== 提取的C#文件内容 ===\n")
        outfile.write(f"提取时间: {os.path.basename(current_dir)}\n")
        outfile.write("=" * 50 + "\n\n")
        
        found_count = 0
        
        for target_file in target_files:
            if os.path.exists(target_file):
                found_count += 1
                print(f"正在处理: {target_file}")
                
                # 写入文件分隔符和文件名
                outfile.write(f"\n{'='*60}\n")
                outfile.write(f"文件: {target_file}\n")
                outfile.write(f"{'='*60}\n\n")
                
                # 读取并写入文件内容
                try:
                    with open(target_file, 'r', encoding='utf-8') as infile:
                        content = infile.read()
                        outfile.write(content)
                        outfile.write("\n")  # 文件末尾添加空行
                except UnicodeDecodeError:
                    # 如果UTF-8解码失败，尝试其他编码
                    try:
                        with open(target_file, 'r', encoding='gbk') as infile:
                            content = infile.read()
                            outfile.write(content)
                            outfile.write("\n")
                    except Exception as e:
                        outfile.write(f"!!! 读取文件时出错: {str(e)}\n")
                except Exception as e:
                    outfile.write(f"!!! 处理文件时出错: {str(e)}\n")
            else:
                print(f"未找到: {target_file}")
                # 在输出文件中记录未找到的文件
                outfile.write(f"\n{'='*60}\n")
                outfile.write(f"!!! 未找到文件: {target_file}\n")
                outfile.write(f"{'='*60}\n\n")
    
    print(f"\n处理完成！")
    print(f"成功提取 {found_count} 个文件")
    print(f"未找到 {len(target_files) - found_count} 个文件")
    print(f"输出文件: {output_file}")
    
    # 显示输出文件的完整路径
    output_path = os.path.join(current_dir, output_file)
    print(f"输出路径: {output_path}")

if __name__ == "__main__":
    extract_cs_files()