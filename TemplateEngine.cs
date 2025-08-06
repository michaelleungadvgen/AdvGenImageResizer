using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AdvGenImageResizer
{
    public class TemplateEngine
    {
        private readonly Dictionary<string, object> _variables = new();

        public void SetVariable(string name, object value)
        {
            _variables[name] = value;
        }

        public void SetVariables(Dictionary<string, object> variables)
        {
            foreach (var kvp in variables)
            {
                _variables[kvp.Key] = kvp.Value;
            }
        }

        public string Render(string template)
        {
            var result = template;

            // Handle loops first (they contain conditionals): {{#each items}}...{{/each}}
            result = ProcessLoops(result);

            // Handle conditionals: {{#if condition}}...{{/if}}
            result = ProcessConditionals(result);

            // Replace simple variables last: {{variableName}}
            result = Regex.Replace(result, @"\{\{(\w+)\}\}", match =>
            {
                var variableName = match.Groups[1].Value;
                var hasVariable = _variables.ContainsKey(variableName);
                return hasVariable ? _variables[variableName]?.ToString() ?? "" : match.Value;
            });

            return result;
        }

        private string ProcessLoops(string template)
        {
            var loopPattern = @"\{\{#each\s+(\w+)\}\}(.*?)\{\{/each\}\}";
            
            return Regex.Replace(template, loopPattern, match =>
            {
                var collectionName = match.Groups[1].Value;
                var loopTemplate = match.Groups[2].Value;

                if (!_variables.ContainsKey(collectionName))
                    return "";

                if (_variables[collectionName] is not IEnumerable<object> collection)
                    return "";

                var result = "";
                var index = 0;

                foreach (var item in collection)
                {
                    var itemTemplate = loopTemplate;
                    
                    // Replace {{this}} with the current item
                    itemTemplate = itemTemplate.Replace("{{this}}", item?.ToString() ?? "");
                    
                    // Replace {{@index}} with current index
                    itemTemplate = itemTemplate.Replace("{{@index}}", index.ToString());
                    
                    // If item is a dictionary, replace properties
                    if (item is Dictionary<string, object> itemDict)
                    {
                        foreach (var kvp in itemDict)
                        {
                            var value = kvp.Value?.ToString() ?? "";
                            itemTemplate = itemTemplate.Replace($"{{{{this.{kvp.Key}}}}}", value);
                        }
                        
                        // Process conditionals within this item context
                        itemTemplate = ProcessItemConditionals(itemTemplate, itemDict);
                    }
                    
                    result += itemTemplate;
                    index++;
                }

                return result;
            }, RegexOptions.Singleline);
        }

        private string ProcessConditionals(string template)
        {
            var result = template;
            var maxIterations = 20;
            var iteration = 0;
            
            while (iteration < maxIterations && result.Contains("{{#if"))
            {
                var hasChanges = false;
                var newResult = ProcessInnermostConditionals(result, ref hasChanges);
                
                if (!hasChanges || newResult == result)
                    break;
                    
                result = newResult;
                iteration++;
            }
            
            return result;
        }

        private string ProcessInnermostConditionals(string template, ref bool hasChanges)
        {
            var result = template;
            
            // Find all {{#if}} positions
            var ifPositions = new List<(int pos, string condition)>();
            var ifPattern = @"\{\{#if\s+(\w+)\}\}";
            var matches = Regex.Matches(result, ifPattern);
            
            foreach (Match match in matches)
            {
                ifPositions.Add((match.Index, match.Groups[1].Value));
            }
            
            // Process each {{#if}} to find complete conditional blocks
            foreach (var (startPos, condition) in ifPositions)
            {
                var block = FindConditionalBlock(result, startPos);
                if (block == null) continue;
                
                var (blockStartPos, blockEndPos, ifContent, elseContent) = block.Value;
                
                // Check if this is an innermost conditional (no nested {{#if}} inside)
                if (IsInnermostConditional(ifContent, elseContent))
                {
                    hasChanges = true;
                    
                    // Evaluate the condition
                    string replacement;
                    if (!_variables.ContainsKey(condition))
                    {
                        replacement = elseContent ?? "";
                    }
                    else
                    {
                        var conditionValue = _variables[condition];
                        var isTruthy = conditionValue switch
                        {
                            null => false,
                            bool b => b,
                            string s => !string.IsNullOrEmpty(s),
                            int i => i != 0,
                            IEnumerable<object> collection => collection.Any(),
                            _ => true
                        };
                        replacement = isTruthy ? ifContent : (elseContent ?? "");
                    }
                    
                    // Replace the entire conditional block
                    result = result.Substring(0, blockStartPos) + replacement + result.Substring(blockEndPos);
                    break; // Process one at a time to avoid position shifts
                }
            }
            
            return result;
        }

        private (int startPos, int endPos, string ifContent, string elseContent)? FindConditionalBlock(string template, int ifStartPos)
        {
            var ifMatch = Regex.Match(template.Substring(ifStartPos), @"\{\{#if\s+\w+\}\}");
            if (!ifMatch.Success) return null;
            
            var contentStart = ifStartPos + ifMatch.Length;
            var depth = 1;
            var pos = contentStart;
            var elsePos = -1;
            
            while (pos < template.Length && depth > 0)
            {
                // Look for {{#if}}, {{else}}, {{/if}}
                var nextIf = template.IndexOf("{{#if", pos);
                var nextElse = template.IndexOf("{{else}}", pos);
                var nextEndIf = template.IndexOf("{{/if}}", pos);
                
                // Find the nearest occurrence
                var nearest = new[] { 
                    (type: "if", pos: nextIf), 
                    (type: "else", pos: nextElse), 
                    (type: "endif", pos: nextEndIf) 
                }
                .Where(x => x.pos >= 0)
                .OrderBy(x => x.pos)
                .FirstOrDefault();
                
                if (nearest.pos < 0) break;
                
                switch (nearest.type)
                {
                    case "if":
                        depth++;
                        pos = nearest.pos + 5; // Skip past {{#if
                        break;
                    case "else":
                        if (depth == 1 && elsePos == -1) // Only capture else at our level
                            elsePos = nearest.pos;
                        pos = nearest.pos + 8; // Skip past {{else}}
                        break;
                    case "endif":
                        depth--;
                        if (depth == 0)
                        {
                            var endPos = nearest.pos + 7; // Past {{/if}}
                            var ifContent = elsePos > 0 
                                ? template.Substring(contentStart, elsePos - contentStart)
                                : template.Substring(contentStart, nearest.pos - contentStart);
                            var elseContent = elsePos > 0 
                                ? template.Substring(elsePos + 8, nearest.pos - elsePos - 8)
                                : null;
                            
                            return (ifStartPos, endPos, ifContent, elseContent);
                        }
                        pos = nearest.pos + 7; // Skip past {{/if}}
                        break;
                }
            }
            
            return null;
        }

        private bool IsInnermostConditional(string ifContent, string elseContent)
        {
            var hasNestedIf = ifContent.Contains("{{#if");
            var hasNestedElseIf = elseContent?.Contains("{{#if") ?? false;
            return !hasNestedIf && !hasNestedElseIf;
        }

        private string ProcessItemConditionals(string template, Dictionary<string, object> itemContext)
        {
            var result = template;
            
            // Handle if-else patterns in item context
            var ifElsePattern = @"\{\{#if\s+(this\.)?(\w+)\}\}(.*?)\{\{else\}\}(.*?)\{\{/if\}\}";
            result = Regex.Replace(result, ifElsePattern, match =>
            {
                var conditionName = match.Groups[2].Value;
                var ifContent = match.Groups[3].Value;
                var elseContent = match.Groups[4].Value;

                // Check if condition exists in item context
                if (!itemContext.ContainsKey(conditionName))
                {
                    return elseContent;
                }

                var conditionValue = itemContext[conditionName];
                
                // Check if condition is truthy
                var isTruthy = conditionValue switch
                {
                    null => false,
                    bool b => b,
                    string s => !string.IsNullOrEmpty(s),
                    int i => i != 0,
                    _ => true
                };
                return isTruthy ? ifContent : elseContent;
            }, RegexOptions.Singleline);
            
            // Handle if-only patterns in item context
            var ifOnlyPattern = @"\{\{#if\s+(this\.)?(\w+)\}\}(.*?)\{\{/if\}\}";
            result = Regex.Replace(result, ifOnlyPattern, match =>
            {
                // Skip if this contains {{else}} (already processed)
                if (match.Groups[3].Value.Contains("{{else}}"))
                    return match.Value;
                    
                var conditionName = match.Groups[2].Value;
                var ifContent = match.Groups[3].Value;

                // Check if condition exists in item context
                if (!itemContext.ContainsKey(conditionName))
                {
                    return "";
                }

                var conditionValue = itemContext[conditionName];
                
                // Check if condition is truthy
                var isTruthy = conditionValue switch
                {
                    null => false,
                    bool b => b,
                    string s => !string.IsNullOrEmpty(s),
                    int i => i != 0,
                    _ => true
                };
                return isTruthy ? ifContent : "";
            }, RegexOptions.Singleline);
            
            return result;
        }

        public void Clear()
        {
            _variables.Clear();
        }
    }

    public class PhotoInfo
    {
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public string RelativePath { get; set; } = "";
        public string ThumbnailPath { get; set; } = "";
        public string FullSizePath { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public DateTime DateTaken { get; set; }
        public long FileSize { get; set; }
        public string FileSizeFormatted { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                ["fileName"] = EscapeJavaScript(FileName),
                ["filePath"] = EscapeJavaScript(FilePath),
                ["relativePath"] = EscapeJavaScript(RelativePath),
                ["thumbnailPath"] = EscapeJavaScript(ThumbnailPath),
                ["fullSizePath"] = EscapeJavaScript(FullSizePath),
                ["title"] = EscapeJavaScript(Title),
                ["description"] = EscapeJavaScript(Description),
                ["dateTaken"] = DateTaken.ToString("yyyy-MM-dd"),
                ["fileSize"] = FileSize,
                ["fileSizeFormatted"] = FileSizeFormatted,
                ["width"] = Width,
                ["height"] = Height
            };
        }
        
        private static string EscapeJavaScript(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "";
            
            return input
                .Replace("\\", "\\\\")  // Escape backslashes first
                .Replace("\"", "\\\"")    // Escape double quotes
                .Replace("'", "\\'")     // Escape single quotes
                .Replace("\r", "\\r")    // Escape carriage return
                .Replace("\n", "\\n")    // Escape newline
                .Replace("\t", "\\t");   // Escape tab
        }
    }

    public class PaginationHelper
    {
        public static List<List<T>> PaginateList<T>(List<T> items, int itemsPerPage)
        {
            var pages = new List<List<T>>();
            
            if (itemsPerPage <= 0)
            {
                pages.Add(items);
                return pages;
            }

            for (int i = 0; i < items.Count; i += itemsPerPage)
            {
                var page = items.Skip(i).Take(itemsPerPage).ToList();
                pages.Add(page);
            }

            return pages;
        }

        public static Dictionary<string, object> CreatePaginationData(int currentPage, int totalPages, string baseFileName)
        {
            var pageNumbers = new List<Dictionary<string, object>>();
            
            // Show up to 7 page numbers with current page in center when possible
            int startPage = Math.Max(1, currentPage - 3);
            int endPage = Math.Min(totalPages, startPage + 6);
            
            if (endPage - startPage < 6)
            {
                startPage = Math.Max(1, endPage - 6);
            }

            for (int i = startPage; i <= endPage; i++)
            {
                pageNumbers.Add(new Dictionary<string, object>
                {
                    ["number"] = i,
                    ["isCurrent"] = i == currentPage,
                    ["file"] = i == 1 ? $"{baseFileName}.html" : $"{baseFileName}_page{i}.html"
                });
            }

            return new Dictionary<string, object>
            {
                ["currentPage"] = currentPage,
                ["totalPages"] = totalPages,
                ["hasPrevPage"] = currentPage > 1,
                ["hasNextPage"] = currentPage < totalPages,
                ["prevPageFile"] = currentPage > 1 ? 
                    (currentPage == 2 ? $"{baseFileName}.html" : $"{baseFileName}_page{currentPage - 1}.html") : "",
                ["nextPageFile"] = currentPage < totalPages ? $"{baseFileName}_page{currentPage + 1}.html" : "",
                ["pageNumbers"] = pageNumbers
            };
        }
    }
}