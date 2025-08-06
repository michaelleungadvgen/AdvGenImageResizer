using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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

        private async void GenerateAlbumButton_Click(object sender, RoutedEventArgs e)
        {
            if (InputFolderPath.Text == "No folder selected..." || string.IsNullOrEmpty(InputFolderPath.Text))
            {
                ShowMessage("Please select an input folder first.");
                return;
            }

            if (_imageFiles.Count == 0)
            {
                ShowMessage("No images found in the selected folder.");
                return;
            }

            var saveDialog = new SaveFileDialog
            {
                Title = "Save Photo Album HTML",
                Filter = "HTML Files (*.html)|*.html",
                FileName = $"{AlbumTitleBox.Text.Replace(" ", "_")}_album.html"
            };

            if (saveDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                await GeneratePhotoAlbum(saveDialog.FileName);
            }
        }

        private async Task GeneratePhotoAlbum(string outputPath)
        {
            try
            {
                ProgressSection.Visibility = Visibility.Visible;
                GenerateAlbumButton.IsEnabled = false;
                StartProcessingButton.IsEnabled = false;

                var templateEngine = new TemplateEngine();
                var photoInfos = new List<PhotoInfo>();

                ProgressText.Text = "Gathering image information...";
                ProcessingProgress.Value = 0;

                // Get pagination settings
                var photosPerPageItem = (ComboBoxItem)PhotosPerPageCombo.SelectedItem;
                int photosPerPage = int.Parse(photosPerPageItem.Tag.ToString());
                bool isPaginated = photosPerPage > 0;
                bool copyOriginalImages = CopyOriginalImagesCheckBox.IsChecked ?? false;

                // Get selected template
                var selectedTemplate = ((ComboBoxItem)TemplateCombo.SelectedItem).Tag.ToString();
                string templateName = isPaginated ? 
                    selectedTemplate.Replace(".html", "-paginated.html") : 
                    selectedTemplate;
                var templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates", templateName);

                if (!File.Exists(templatePath))
                {
                    ShowMessage($"Template file not found: {templatePath}");
                    return;
                }

                // Create output directory for images if copying originals
                string outputDir = Path.GetDirectoryName(outputPath);
                string imagesDir = Path.Combine(outputDir, "images");
                
                if (copyOriginalImages)
                {
                    Directory.CreateDirectory(imagesDir);
                }

                // Process each image to get metadata
                for (int i = 0; i < _imageFiles.Count; i++)
                {
                    var imagePath = _imageFiles[i];
                    var fileName = Path.GetFileName(imagePath);
                    
                    ProgressText.Text = $"Processing {fileName} ({i + 1}/{_imageFiles.Count})";
                    ProcessingProgress.Value = ((double)i / _imageFiles.Count) * (copyOriginalImages ? 50 : 75);

                    try
                    {
                        var photoInfo = await Task.Run(() => GetPhotoInfo(imagePath, imagesDir, copyOriginalImages));
                        photoInfos.Add(photoInfo);
                    }
                    catch (Exception)
                    {
                        // Skip problematic images but continue processing
                        continue;
                    }

                    await Task.Delay(10);
                }

                if (copyOriginalImages)
                {
                    ProgressText.Text = "Copying original images...";
                    await CopyOriginalImages(photoInfos, imagesDir);
                    ProcessingProgress.Value = 75;
                }

                ProgressText.Text = "Generating HTML album...";
                
                string baseFileName = Path.GetFileNameWithoutExtension(outputPath);
                
                if (isPaginated)
                {
                    await GeneratePaginatedAlbum(templatePath, photoInfos, outputPath, baseFileName, photosPerPage);
                }
                else
                {
                    await GenerateSinglePageAlbum(templatePath, photoInfos, outputPath);
                }

                ProgressText.Text = "Album generation completed!";
                ProcessingProgress.Value = 100;

                var albumInfo = isPaginated ? 
                    $"Photo album generated successfully!\n\nSaved to: {outputDir}\n\nThe album contains {photoInfos.Count} photos across {PaginationHelper.PaginateList(photoInfos, photosPerPage).Count} pages." :
                    $"Photo album generated successfully!\n\nSaved to: {outputPath}\n\nThe album contains {photoInfos.Count} photos.";
                
                if (copyOriginalImages)
                {
                    albumInfo += $"\n\nOriginal images copied to: {imagesDir}";
                }
                
                ShowMessage(albumInfo);
            }
            catch (Exception ex)
            {
                ShowMessage($"Error generating photo album: {ex.Message}");
                ProgressText.Text = "Album generation failed.";
            }
            finally
            {
                GenerateAlbumButton.IsEnabled = true;
                StartProcessingButton.IsEnabled = true;
            }
        }

        private PhotoInfo GetPhotoInfo(string imagePath, string imagesDir = null, bool copyOriginals = false)
        {
            var fileInfo = new FileInfo(imagePath);
            var fileName = fileInfo.Name;
            
            var photoInfo = new PhotoInfo
            {
                FileName = fileName,
                FilePath = imagePath,
                RelativePath = copyOriginals ? $"images/{fileName}" : fileName,
                ThumbnailPath = copyOriginals ? $"images/{fileName}" : fileName,
                FullSizePath = copyOriginals ? $"images/{fileName}" : fileName,
                Title = Path.GetFileNameWithoutExtension(imagePath),
                Description = "",
                DateTaken = fileInfo.LastWriteTime,
                FileSize = fileInfo.Length,
                FileSizeFormatted = FormatFileSize(fileInfo.Length)
            };

            // Get image dimensions
            try
            {
                var dimensions = GetImageDimensions(imagePath);
                photoInfo.Width = dimensions.Width;
                photoInfo.Height = dimensions.Height;
            }
            catch
            {
                photoInfo.Width = 0;
                photoInfo.Height = 0;
            }

            return photoInfo;
        }

        private async Task CopyOriginalImages(List<PhotoInfo> photoInfos, string imagesDir)
        {
            for (int i = 0; i < photoInfos.Count; i++)
            {
                var photoInfo = photoInfos[i];
                var destPath = Path.Combine(imagesDir, photoInfo.FileName);
                
                ProgressText.Text = $"Copying {photoInfo.FileName} ({i + 1}/{photoInfos.Count})";
                ProcessingProgress.Value = 50 + ((double)i / photoInfos.Count) * 25;
                
                try
                {
                    await Task.Run(() => File.Copy(photoInfo.FilePath, destPath, overwrite: true));
                }
                catch (Exception)
                {
                    // Log error but continue with other images
                    continue;
                }
                
                await Task.Delay(10);
            }
        }

        private async Task GenerateSinglePageAlbum(string templatePath, List<PhotoInfo> photoInfos, string outputPath)
        {
            var templateContent = await File.ReadAllTextAsync(templatePath);
            var templateEngine = new TemplateEngine();
            
            templateEngine.SetVariable("albumTitle", AlbumTitleBox.Text);
            templateEngine.SetVariable("albumDescription", AlbumDescriptionBox.Text);
            templateEngine.SetVariable("photoCount", photoInfos.Count);
            templateEngine.SetVariable("totalPhotoCount", photoInfos.Count);
            templateEngine.SetVariable("currentPagePhotoCount", photoInfos.Count);
            templateEngine.SetVariable("dateGenerated", DateTime.Now.ToString("yyyy-MM-dd"));
            templateEngine.SetVariable("photos", photoInfos.Select(p => p.ToDictionary()).Cast<object>().ToList());
            templateEngine.SetVariable("isPaginated", false);
            templateEngine.SetVariable("hasPrevPage", false);
            templateEngine.SetVariable("hasNextPage", false);
            
            var htmlContent = templateEngine.Render(templateContent);
            await File.WriteAllTextAsync(outputPath, htmlContent);
            
            ProcessingProgress.Value = 90;
        }

        private async Task GeneratePaginatedAlbum(string templatePath, List<PhotoInfo> photoInfos, string outputPath, string baseFileName, int photosPerPage)
        {
            var templateContent = await File.ReadAllTextAsync(templatePath);
            var pages = PaginationHelper.PaginateList(photoInfos, photosPerPage);
            var outputDir = Path.GetDirectoryName(outputPath);
            
            for (int pageIndex = 0; pageIndex < pages.Count; pageIndex++)
            {
                var currentPage = pageIndex + 1;
                var pagePhotos = pages[pageIndex];
                var templateEngine = new TemplateEngine();
                
                // Set basic template variables
                templateEngine.SetVariable("albumTitle", AlbumTitleBox.Text);
                templateEngine.SetVariable("albumDescription", AlbumDescriptionBox.Text);
                templateEngine.SetVariable("totalPhotoCount", photoInfos.Count);
                templateEngine.SetVariable("currentPagePhotoCount", pagePhotos.Count);
                templateEngine.SetVariable("dateGenerated", DateTime.Now.ToString("yyyy-MM-dd"));
                templateEngine.SetVariable("photos", pagePhotos.Select(p => p.ToDictionary()).Cast<object>().ToList());
                
                // Set pagination variables
                templateEngine.SetVariable("isPaginated", true);
                var paginationData = PaginationHelper.CreatePaginationData(currentPage, pages.Count, baseFileName);
                
                // Debug output
                System.Diagnostics.Debug.WriteLine($"Page {currentPage} of {pages.Count}:");
                System.Diagnostics.Debug.WriteLine($"isPaginated: true");
                foreach (var kvp in paginationData)
                {
                    // Special handling for pageNumbers array
                    if (kvp.Key == "pageNumbers" && kvp.Value is List<Dictionary<string, object>> pageNumbersList)
                    {
                        templateEngine.SetVariable(kvp.Key, pageNumbersList.Cast<object>().ToList());
                        System.Diagnostics.Debug.WriteLine($"{kvp.Key}: List with {pageNumbersList.Count} items");
                    }
                    else
                    {
                        templateEngine.SetVariable(kvp.Key, kvp.Value);
                        System.Diagnostics.Debug.WriteLine($"{kvp.Key}: {kvp.Value}");
                    }
                }
                
                // Generate HTML for this page
                var htmlContent = templateEngine.Render(templateContent);
                
                // Save page file
                var pageFileName = currentPage == 1 ? 
                    $"{baseFileName}.html" : 
                    $"{baseFileName}_page{currentPage}.html";
                var pageFilePath = Path.Combine(outputDir, pageFileName);
                
                await File.WriteAllTextAsync(pageFilePath, htmlContent);
                
                ProgressText.Text = $"Generated page {currentPage} of {pages.Count}";
                ProcessingProgress.Value = 75 + ((double)pageIndex / pages.Count) * 15;
                
                await Task.Delay(10);
            }
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private void ShowMessage(string message)
        {
            System.Windows.MessageBox.Show(message, "AdvGen Image Resizer", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}