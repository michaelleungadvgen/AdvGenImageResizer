using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using SkiaSharp;
using Image = System.Drawing.Image;

namespace AdvGenImageResizer
{
    public partial class MainWindow : Window
    {
        private List<string> _imageFiles = new();
        private readonly string[] _supportedExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff" };

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void BrowseInputButton_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select folder containing images to resize",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                InputFolderPath.Text = dialog.SelectedPath;
                await ScanForImages(dialog.SelectedPath);
            }
        }

        private void BrowseOutputButton_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select output folder for resized images",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                OutputFolderPath.Text = dialog.SelectedPath;
            }
        }

        private async Task ScanForImages(string folderPath)
        {
            try
            {
                _imageFiles.Clear();
                
                var (imageFiles, largeImageCount) = await Task.Run(() =>
                {
                    var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                                       .Where(file => _supportedExtensions.Contains(Path.GetExtension(file).ToLower()))
                                       .ToList();
                    
                    int largeImages = 0;
                    foreach (var file in files)
                    {
                        try
                        {
                            var dimensions = GetImageDimensions(file);
                            long estimatedMemory = (long)dimensions.Width * dimensions.Height * 4;
                            if (estimatedMemory > 200 * 1024 * 1024) // 200MB threshold for warning
                            {
                                largeImages++;
                            }
                        }
                        catch
                        {
                            // Skip files we can't read
                        }
                    }
                    
                    return (files, largeImages);
                });
                
                _imageFiles.AddRange(imageFiles);

                if (largeImageCount > 0)
                {
                    ImageCountText.Text = $"{_imageFiles.Count} image(s) found ({largeImageCount} very large images detected)";
                }
                else
                {
                    ImageCountText.Text = $"{_imageFiles.Count} image(s) found";
                }
            }
            catch (Exception ex)
            {
                ImageCountText.Text = $"Error scanning folder: {ex.Message}";
            }
        }

        private async void StartProcessingButton_Click(object sender, RoutedEventArgs e)
        {
            if (InputFolderPath.Text == "No folder selected..." || string.IsNullOrEmpty(InputFolderPath.Text))
            {
                ShowMessage("Please select an input folder.");
                return;
            }

            if (OutputFolderPath.Text == "No folder selected..." || string.IsNullOrEmpty(OutputFolderPath.Text))
            {
                ShowMessage("Please select an output folder.");
                return;
            }

            if (_imageFiles.Count == 0)
            {
                ShowMessage("No images found in the selected folder.");
                return;
            }

            if (!int.TryParse(WidthBox.Text, out int width) || width <= 0)
            {
                ShowMessage("Please enter a valid width.");
                return;
            }

            if (!int.TryParse(HeightBox.Text, out int height) || height <= 0)
            {
                ShowMessage("Please enter a valid height.");
                return;
            }

            await ProcessImages();
        }

        private async Task ProcessImages()
        {
            ProgressSection.Visibility = Visibility.Visible;
            StartProcessingButton.IsEnabled = false;
            BrowseInputButton.IsEnabled = false;
            BrowseOutputButton.IsEnabled = false;

            try
            {
                var targetWidth = int.Parse(WidthBox.Text);
                var targetHeight = int.Parse(HeightBox.Text);
                var maintainAspectRatio = MaintainAspectRatio.IsChecked ?? false;
                var resizeMode = ResizeModeCombo.SelectedIndex;

                int successCount = 0;
                int failureCount = 0;
                var failedFiles = new List<string>();

                for (int i = 0; i < _imageFiles.Count; i++)
                {
                    var inputFile = _imageFiles[i];
                    var fileName = Path.GetFileName(inputFile);
                    var outputFile = Path.Combine(OutputFolderPath.Text, fileName);

                    ProgressText.Text = $"Processing {fileName} ({i + 1}/{_imageFiles.Count})";
                    ProcessingProgress.Value = ((double)(i + 1) / _imageFiles.Count) * 100;

                    try
                    {
                        await Task.Run(() => ResizeImage(inputFile, outputFile, targetWidth, targetHeight, maintainAspectRatio, resizeMode));
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        failureCount++;
                        failedFiles.Add($"{fileName}: {ex.Message}");
                        
                        // Continue processing other files
                        continue;
                    }

                    await Task.Delay(10);
                }

                // Show final results
                if (failureCount == 0)
                {
                    ProgressText.Text = $"Completed! Successfully processed {successCount} images.";
                    ShowMessage($"Successfully processed all {successCount} images!");
                }
                else
                {
                    ProgressText.Text = $"Completed with {failureCount} errors. {successCount} images processed successfully.";
                    var message = $"Processed {successCount} images successfully.\n{failureCount} images failed:\n\n" +
                                 string.Join("\n", failedFiles.Take(5)); // Show first 5 failures
                    if (failedFiles.Count > 5)
                    {
                        message += $"\n... and {failedFiles.Count - 5} more.";
                    }
                    ShowMessage(message);
                }
            }
            catch (Exception ex)
            {
                ProgressText.Text = "Error occurred during processing.";
                ShowMessage($"Error: {ex.Message}");
            }
            finally
            {
                StartProcessingButton.IsEnabled = true;
                BrowseInputButton.IsEnabled = true;
                BrowseOutputButton.IsEnabled = true;
            }
        }

        private void ResizeImage(string inputPath, string outputPath, int targetWidth, int targetHeight, bool maintainAspectRatio, int resizeMode)
        {
            try
            {
                // Check if the image is too large before processing
                var imageSize = GetImageDimensions(inputPath);
                long estimatedMemory = (long)imageSize.Width * imageSize.Height * 4; // 4 bytes per pixel for ARGB
                
                // If image would require more than 100MB, use ImageSharp approach
                if (estimatedMemory > 100 * 1024 * 1024)
                {
                    ResizeLargeImage(inputPath, outputPath, targetWidth, targetHeight, maintainAspectRatio, resizeMode);
                    return;
                }

                using var originalImage = Image.FromFile(inputPath);
                
                int newWidth = targetWidth;
                int newHeight = targetHeight;

                if (maintainAspectRatio && resizeMode == 0)
                {
                    double ratioX = (double)targetWidth / originalImage.Width;
                    double ratioY = (double)targetHeight / originalImage.Height;
                    double ratio = Math.Min(ratioX, ratioY);

                    newWidth = (int)(originalImage.Width * ratio);
                    newHeight = (int)(originalImage.Height * ratio);
                }
                else if (maintainAspectRatio && resizeMode == 1)
                {
                    double ratioX = (double)targetWidth / originalImage.Width;
                    double ratioY = (double)targetHeight / originalImage.Height;
                    double ratio = Math.Max(ratioX, ratioY);

                    newWidth = (int)(originalImage.Width * ratio);
                    newHeight = (int)(originalImage.Height * ratio);
                }

                // Ensure we don't create extremely large output images
                if (newWidth > 10000 || newHeight > 10000)
                {
                    throw new Exception("Output dimensions are too large. Please use smaller target dimensions.");
                }

                using var resizedImage = new Bitmap(newWidth, newHeight);
                using var graphics = Graphics.FromImage(resizedImage);
                
                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

                graphics.DrawImage(originalImage, 0, 0, newWidth, newHeight);

                SaveImage(resizedImage, outputPath, Path.GetExtension(inputPath).ToLower());
            }
            catch (OutOfMemoryException)
            {
                // Try with a more memory-efficient approach
                try
                {
                    ResizeLargeImage(inputPath, outputPath, targetWidth, targetHeight, maintainAspectRatio, resizeMode);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to resize {Path.GetFileName(inputPath)} - image too large: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to resize {Path.GetFileName(inputPath)}: {ex.Message}");
            }
        }

        private (int Width, int Height) GetImageDimensions(string imagePath)
        {
            using var image = Image.FromFile(imagePath);
            return (image.Width, image.Height);
        }

        private void ResizeLargeImage(string inputPath, string outputPath, int targetWidth, int targetHeight, bool maintainAspectRatio, int resizeMode)
        {
            try
            {
                // Use SkiaSharp for memory-efficient processing
                ResizeWithSkiaSharp(inputPath, outputPath, targetWidth, targetHeight, maintainAspectRatio, resizeMode);
            }
            catch (Exception)
            {
                // If ImageProcessor fails, fall back to System.Drawing
                ResizeWithSystemDrawingConservative(inputPath, outputPath, targetWidth, targetHeight, maintainAspectRatio, resizeMode);
            }
            finally
            {
                // Force garbage collection after processing large images
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        private void ResizeWithSkiaSharp(string inputPath, string outputPath, int targetWidth, int targetHeight, bool maintainAspectRatio, int resizeMode)
        {
            using var inputStream = File.OpenRead(inputPath);
            using var inputBitmap = SKBitmap.Decode(inputStream);
            
            if (inputBitmap == null)
                throw new Exception("Could not decode image file");
            
            int originalWidth = inputBitmap.Width;
            int originalHeight = inputBitmap.Height;
            
            int newWidth = targetWidth;
            int newHeight = targetHeight;

            if (maintainAspectRatio && resizeMode == 0) // Fit mode
            {
                double ratioX = (double)targetWidth / originalWidth;
                double ratioY = (double)targetHeight / originalHeight;
                double ratio = Math.Min(ratioX, ratioY);

                newWidth = (int)(originalWidth * ratio);
                newHeight = (int)(originalHeight * ratio);
            }
            else if (maintainAspectRatio && resizeMode == 1) // Fill mode
            {
                double ratioX = (double)targetWidth / originalWidth;
                double ratioY = (double)targetHeight / originalHeight;
                double ratio = Math.Max(ratioX, ratioY);

                newWidth = (int)(originalWidth * ratio);
                newHeight = (int)(originalHeight * ratio);
            }

            // Create resized bitmap
            var imageInfo = new SKImageInfo(newWidth, newHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var resizedBitmap = inputBitmap.Resize(imageInfo, SKFilterQuality.High);
            
            if (resizedBitmap == null)
                throw new Exception("Failed to resize image");

            // Save the image
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            var extension = Path.GetExtension(inputPath).ToLower();
            SKEncodedImageFormat format;
            int quality = 90;

            switch (extension)
            {
                case ".jpg":
                case ".jpeg":
                    format = SKEncodedImageFormat.Jpeg;
                    break;
                case ".png":
                    format = SKEncodedImageFormat.Png;
                    quality = 100; // PNG doesn't use quality, but we set it anyway
                    break;
                case ".bmp":
                case ".gif":
                case ".tiff":
                default:
                    // Convert to JPEG for compatibility
                    format = SKEncodedImageFormat.Jpeg;
                    outputPath = Path.ChangeExtension(outputPath, ".jpg");
                    break;
            }

            using var image = SKImage.FromBitmap(resizedBitmap);
            using var data = image.Encode(format, quality);
            using var outputStream = File.OpenWrite(outputPath);
            data.SaveTo(outputStream);
        }

        private void ResizeWithSystemDrawingConservative(string inputPath, string outputPath, int targetWidth, int targetHeight, bool maintainAspectRatio, int resizeMode)
        {
            try
            {
                using var originalImage = Image.FromFile(inputPath);
                
                // Limit output size to prevent memory issues
                int maxSize = 4000;
                if (targetWidth > maxSize) targetWidth = maxSize;
                if (targetHeight > maxSize) targetHeight = maxSize;
                
                int newWidth = targetWidth;
                int newHeight = targetHeight;

                if (maintainAspectRatio && resizeMode == 0)
                {
                    double ratioX = (double)targetWidth / originalImage.Width;
                    double ratioY = (double)targetHeight / originalImage.Height;
                    double ratio = Math.Min(ratioX, ratioY);

                    newWidth = (int)(originalImage.Width * ratio);
                    newHeight = (int)(originalImage.Height * ratio);
                }

                using var resizedImage = new Bitmap(newWidth, newHeight);
                using var graphics = Graphics.FromImage(resizedImage);
                
                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

                graphics.DrawImage(originalImage, 0, 0, newWidth, newHeight);

                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                SaveImage(resizedImage, outputPath, Path.GetExtension(inputPath).ToLower());
            }
            catch (Exception ex)
            {
                throw new Exception($"Image processing failed: {ex.Message}");
            }
        }

        private void SaveImage(Bitmap image, string outputPath, string extension)
        {
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            var encoder = GetEncoder(extension);
            if (encoder != null)
            {
                var encoderParameters = new EncoderParameters(1);
                encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L); // Slightly lower quality to save space
                image.Save(outputPath, encoder, encoderParameters);
            }
            else
            {
                image.Save(outputPath);
            }
        }

        private ImageCodecInfo GetEncoder(string extension)
        {
            var mimeType = extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".bmp" => "image/bmp",
                ".gif" => "image/gif",
                ".tiff" => "image/tiff",
                _ => "image/jpeg" // Default to JPEG for better compatibility
            };

            var codecs = ImageCodecInfo.GetImageEncoders();
            return codecs.FirstOrDefault(codec => codec.MimeType == mimeType) ?? codecs.First(codec => codec.MimeType == "image/jpeg");
        }

        private void ShowMessage(string message)
        {
            System.Windows.MessageBox.Show(message, "AdvGen Image Resizer", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}