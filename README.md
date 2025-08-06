# AdvGen Image Resizer

A professional WPF image resizer application with WinUI-style design, built for handling images of all sizes - from small photos to extremely large AI-generated images.

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)
![Platform](https://img.shields.io/badge/platform-Windows-lightgrey.svg)

## ✨ Features

- **🎨 Modern WinUI Design**: Clean, professional interface matching Microsoft's design language
- **📁 Batch Processing**: Process entire folders of images at once
- **🚀 Smart Memory Management**: Handles extremely large images (including AI-generated content) without crashes
- **⚙️ Flexible Resize Options**: 
  - Width and height controls
  - Maintain aspect ratio option
  - Three resize modes: Fit, Fill, and Stretch
- **📊 Real-time Progress**: Live progress tracking with detailed status updates
- **🔄 Multi-format Support**: JPEG, PNG, BMP, GIF, TIFF
- **🛡️ Robust Error Handling**: Individual file failures don't stop batch processing
- **💾 Format Preservation**: Maintains original image formats or converts problematic files to JPEG

## 🏗️ Architecture

### Multi-Tier Processing System

1. **Standard Processing** (< 100MB): Uses System.Drawing for optimal quality
2. **Large Image Processing** (> 100MB): Uses ImageSharp for memory efficiency  
3. **Progressive Fallback**: Handles extremely problematic images with step-down approach
4. **Conservative Backup**: Final fallback with maximum compatibility

### Memory Management
- **Smart detection**: Pre-calculates memory requirements
- **Streaming processing**: Efficient handling of large files
- **Garbage collection**: Aggressive cleanup after large image processing
- **Progressive sizing**: Multi-step resizing for massive images

## 🚀 Getting Started

### Prerequisites
- Windows 10/11
- .NET 8.0 Runtime

### Installation

1. **Download the latest release** or clone this repository:
```bash
git clone https://github.com/your-username/AdvGenImageResizer.git
cd AdvGenImageResizer
```

2. **Build the application**:
```bash
dotnet build
```

3. **Run the application**:
```bash
dotnet run
```

### Usage

1. **Select Input Folder**: Click "Browse" next to "Input Folder" to select your image folder
2. **Configure Settings**: 
   - Set target width and height
   - Choose whether to maintain aspect ratio
   - Select resize mode (Fit/Fill/Stretch)
3. **Select Output Folder**: Choose where to save resized images
4. **Start Processing**: Click "Start Processing" to begin batch resize

## 🛠️ Technical Details

### Dependencies
- **.NET 8.0**: Modern, high-performance framework
- **WPF**: Windows Presentation Foundation for rich UI
- **System.Drawing.Common**: Standard image processing
- **SixLabors.ImageSharp**: Advanced, memory-efficient image processing
- **Windows Forms**: For folder selection dialogs

### Supported Formats
- **Input**: JPEG, PNG, BMP, GIF, TIFF
- **Output**: Same as input, or JPEG for better compatibility

### Performance Features
- **Parallel processing**: Non-blocking UI during batch operations
- **Memory optimization**: Multiple fallback strategies for large images
- **Format-specific handling**: Optimized encoding for each image type
- **Progress reporting**: Real-time status updates

## 🎯 Use Cases

- **📷 Photography**: Perfect for batch resizing your personal photo collections
- **📁 Archive Management**: Standardize image sizes in photo archives
- **🌐 Web Development**: Prepare photos for web use
- **📱 Social Media**: Resize photos for various social platforms
- **💾 Storage Optimization**: Reduce file sizes while maintaining quality

## 🤝 Contributing

We welcome contributions! Please feel free to submit issues, feature requests, or pull requests.

### Development Setup
1. Clone the repository
2. Open `AdvGenImageResizer.sln` in Visual Studio 2022
3. Build and run the project

### Code Style
- Follow C# conventions
- Use meaningful variable names  
- Comment complex logic
- Maintain the existing architecture pattern

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

### Third-Party Dependencies

This application uses several third-party libraries, all with MIT license:

- **Microsoft .NET 8.0, WPF, Windows Forms, System.Drawing.Common**: MIT License
- **SkiaSharp**: MIT License

**Perfect License Harmony**: All components use MIT licensing, making this application completely free for both personal and commercial use.

For complete license information of all dependencies, see [THIRD-PARTY-LICENSES.md](THIRD-PARTY-LICENSES.md).

## 🙏 Acknowledgments

- **SkiaSharp**: Powerful MIT-licensed cross-platform 2D graphics library by Microsoft/Xamarin
- **Microsoft WinUI**: Design inspiration
- **AI Community**: For creating the large images that inspired robust memory handling

## 🐛 Issues & Support

If you encounter any issues or have questions:

1. Check existing [Issues](../../issues)
2. Create a new issue with:
   - Detailed description
   - Steps to reproduce
   - System information
   - Sample images (if applicable)

## 🔄 Changelog

### Version 1.0.0
- ✅ Initial release
- ✅ WinUI-style interface
- ✅ Multi-tier image processing
- ✅ Comprehensive error handling
- ✅ Support for extremely large images
- ✅ MIT License

---

**Made with ❤️ for the creative community**