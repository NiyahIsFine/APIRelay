using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace APIRelay
{
    public partial class Form1
    {
        private UsageInfo ExtractUsage(byte[] responseBytes, string? mediaType)
        {
            if (responseBytes.Length == 0)
            {
                return UsageInfo.Empty;
            }

            var body = Encoding.UTF8.GetString(responseBytes);
            if (mediaType?.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase) == true || body.Contains("data:", StringComparison.Ordinal))
            {
                return ExtractStreamingUsage(body);
            }

            return ExtractJsonUsage(body);
        }

        private static UsageInfo ExtractJsonUsage(string body)
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                return TryReadUsage(document.RootElement, out var usage) ? usage : UsageInfo.Empty;
            }
            catch (JsonException)
            {
                return UsageInfo.Empty;
            }
        }

        private static UsageInfo ExtractStreamingUsage(string body)
        {
            var result = UsageInfo.Empty;
            var eventData = new StringBuilder();

            foreach (var rawLine in body.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0)
                {
                    result = MergeUsage(result, ExtractUsageFromEventData(eventData.ToString()));
                    eventData.Clear();
                    continue;
                }

                if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var data = line[5..].TrimStart();
                eventData.AppendLine(data);
            }

            result = MergeUsage(result, ExtractUsageFromEventData(eventData.ToString()));

            return result;
        }

        private static UsageInfo ExtractUsageFromEventData(string eventData)
        {
            var json = eventData.Trim();
            if (json.Length == 0 || json.Equals("[DONE]", StringComparison.OrdinalIgnoreCase))
            {
                return UsageInfo.Empty;
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                return TryReadUsage(document.RootElement, out var usage) ? usage : UsageInfo.Empty;
            }
            catch (JsonException)
            {
                return UsageInfo.Empty;
            }
        }

        private static bool TryReadUsage(JsonElement root, out UsageInfo usage)
        {
            usage = UsageInfo.Empty;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (TryReadUsageFromElement(root, out usage))
            {
                return true;
            }

            if (root.TryGetProperty("response", out var responseElement) && TryReadUsageFromElement(responseElement, out usage))
            {
                return true;
            }

            return false;
        }

        private static bool TryReadUsageFromElement(JsonElement root, out UsageInfo usage)
        {
            usage = UsageInfo.Empty;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!root.TryGetProperty("usage", out var usageElement) || usageElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return false;
            }

            if (usageElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var prompt = ReadInt(usageElement, "prompt_tokens", "input_tokens");
            var completion = ReadInt(usageElement, "completion_tokens", "output_tokens");
            var cacheTokensSeparateFromInput = usageElement.TryGetProperty("cache_read_input_tokens", out _)
                || usageElement.TryGetProperty("cache_creation_input_tokens", out _);
            var cached = ReadCachedTokens(usageElement);
            var cacheCreation = ReadCacheCreationTokens(usageElement);
            var total = ReadInt(usageElement, "total_tokens");

            if (total == 0)
            {
                total = prompt + completion + cacheCreation;
            }

            usage = new UsageInfo(prompt, completion, cached, cacheCreation, total, ReadString(root, "model"), cacheTokensSeparateFromInput);
            return total > 0 || prompt > 0 || completion > 0 || cached > 0 || cacheCreation > 0;
        }

        private static UsageInfo MergeUsage(UsageInfo current, UsageInfo next)
        {
            var prompt = Math.Max(current.PromptTokens, next.PromptTokens);
            var completion = Math.Max(current.CompletionTokens, next.CompletionTokens);
            var cached = Math.Max(current.CachedTokens, next.CachedTokens);
            var cacheCreation = Math.Max(current.CacheCreationTokens, next.CacheCreationTokens);
            var total = Math.Max(current.TotalTokens, next.TotalTokens);

            if (total == 0)
            {
                total = prompt + completion + cacheCreation;
            }

            var model = string.IsNullOrEmpty(next.Model) ? current.Model : next.Model;
            return new UsageInfo(prompt, completion, cached, cacheCreation, total, model, current.CacheTokensSeparateFromInput || next.CacheTokensSeparateFromInput);
        }

        private static int ReadCachedTokens(JsonElement usageElement)
        {
            if (usageElement.ValueKind != JsonValueKind.Object)
            {
                return 0;
            }

            var cached = ReadInt(usageElement, "cache_read_input_tokens");

            if (usageElement.TryGetProperty("input_tokens_details", out var inputDetails))
            {
                cached = Math.Max(cached, ReadInt(inputDetails, "cached_tokens"));
            }

            if (usageElement.TryGetProperty("prompt_tokens_details", out var promptDetails))
            {
                cached = Math.Max(cached, ReadInt(promptDetails, "cached_tokens"));
            }

            cached = Math.Max(cached, ReadNestedInt(usageElement, "cached_tokens", "cache_read_input_tokens"));
            return cached;
        }

        private static int ReadCacheCreationTokens(JsonElement usageElement)
        {
            if (usageElement.ValueKind != JsonValueKind.Object)
            {
                return 0;
            }

            var cacheCreation = ReadInt(usageElement, "cache_creation_input_tokens");

            if (usageElement.TryGetProperty("input_tokens_details", out var inputDetails))
            {
                cacheCreation = Math.Max(cacheCreation, ReadInt(inputDetails, "cache_creation_tokens", "cache_creation_input_tokens"));
            }

            if (usageElement.TryGetProperty("prompt_tokens_details", out var promptDetails))
            {
                cacheCreation = Math.Max(cacheCreation, ReadInt(promptDetails, "cache_creation_tokens", "cache_creation_input_tokens"));
            }

            cacheCreation = Math.Max(cacheCreation, ReadNestedInt(usageElement, "cache_creation_tokens", "cache_creation_input_tokens"));
            return cacheCreation;
        }

        private static int ReadNestedInt(JsonElement element, params string[] propertyNames)
        {
            var result = ReadInt(element, propertyNames);

            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    result = Math.Max(result, ReadNestedInt(property.Value, propertyNames));
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    result = Math.Max(result, ReadNestedInt(item, propertyNames));
                }
            }

            return result;
        }

        private static int ReadInt(JsonElement element, params string[] propertyNames)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return 0;
            }

            foreach (var propertyName in propertyNames)
            {
                if (element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var number))
                {
                    return number;
                }
            }

            return 0;
        }

        private static string ReadString(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        private void RecordRequest(HttpListenerRequest request, string providerPath, int statusCode, UsageInfo usage, long elapsedMs, long firstResponseMs, byte[] requestBody)
        {
            if (!HasBillableUsage(usage))
            {
                return;
            }

            var record = new RequestRecord
            {
                Timestamp = DateTime.Now,
                Model = string.IsNullOrWhiteSpace(usage.Model) ? ExtractRequestModel(requestBody) : usage.Model,
                Path = string.IsNullOrWhiteSpace(providerPath) ? request.Url?.PathAndQuery ?? string.Empty : providerPath,
                Summary = BuildRequestSummary(request, requestBody),
                StatusCode = statusCode,
                PromptTokens = usage.PromptTokens,
                CompletionTokens = usage.CompletionTokens,
                CachedTokens = usage.CachedTokens,
                CacheCreationTokens = usage.CacheCreationTokens,
                CacheTokensSeparateFromInput = usage.CacheTokensSeparateFromInput,
                TotalTokens = usage.TotalTokens,
                ElapsedMs = elapsedMs,
                FirstResponseMs = firstResponseMs
            };

            var isFirstRecordForDate = SaveRequestRecord(record);

            TryBeginInvoke(() =>
            {
                if (record.Timestamp.Date == DateTime.Today && isFirstRecordForDate)
                {
                    LoadRecordDates(DateTime.Today);
                    LoadRecordsForSelectedDate();
                    usageBubble?.ShowCostToast(CalculateRecordCost(record));
                    return;
                }

                AddRecordDate(record.Timestamp.Date);

                if (GetSelectedRecordDate() == record.Timestamp.Date)
                {
                    visibleRecords.Insert(0, record);
                    InsertRequestGridRow(0, record);
                }

                UpdateTotals();
                UpdateUsageBubble();
                usageBubble?.ShowCostToast(CalculateRecordCost(record));
                dailyChartPanel.Invalidate();
            });
        }

        private static bool HasBillableUsage(UsageInfo usage)
        {
            return usage.PromptTokens > 0 || usage.CompletionTokens > 0 || usage.CacheCreationTokens > 0;
        }

        private static string FormatInputTokens(RequestRecord record)
        {
            return $"{CalculateNonCachedReadInputTokens(record)}/{record.CachedTokens}";
        }

        private decimal CalculateRecordCost(RequestRecord record)
        {
            var modelCost = FindModelCost(record.Model);
            if (modelCost == null)
            {
                return 0m;
            }

            var billableInputTokens = CalculateBillableInputTokens(record);
            return (billableInputTokens * modelCost.InputCostPerMillion
                + record.CompletionTokens * modelCost.OutputCostPerMillion
                + record.CachedTokens * modelCost.CacheHitCostPerMillion
                + record.CacheCreationTokens * modelCost.CacheCreationCostPerMillion) / 1_000_000m;
        }

        private string BuildCostFormulaText(RequestRecord record)
        {
            var billableInputTokens = CalculateBillableInputTokens(record);
            var modelCost = FindModelCost(record.Model);
            if (modelCost == null)
            {
                return GetText(TextId.Txt78, record.Model);
            }

            var inputCost = billableInputTokens * modelCost.InputCostPerMillion / 1_000_000m;
            var outputCost = record.CompletionTokens * modelCost.OutputCostPerMillion / 1_000_000m;
            var cacheHitCost = record.CachedTokens * modelCost.CacheHitCostPerMillion / 1_000_000m;
            var cacheCreationCost = record.CacheCreationTokens * modelCost.CacheCreationCostPerMillion / 1_000_000m;
            var totalCost = inputCost + outputCost + cacheHitCost + cacheCreationCost;
            var inputMode = record.CacheTokensSeparateFromInput
                ? GetText(TextId.Txt79)
                : GetText(TextId.Txt80);

            return string.Join("\r\n", new[]
            {
                GetText(TextId.Txt81, record.Model),
                inputMode,
                GetText(TextId.Txt82, billableInputTokens, modelCost.InputCostPerMillion, FormatCurrency(inputCost)),
                GetText(TextId.Txt83, record.CompletionTokens, modelCost.OutputCostPerMillion, FormatCurrency(outputCost)),
                GetText(TextId.Txt84, record.CachedTokens, modelCost.CacheHitCostPerMillion, FormatCurrency(cacheHitCost)),
                GetText(TextId.Txt85, record.CacheCreationTokens, modelCost.CacheCreationCostPerMillion, FormatCurrency(cacheCreationCost)),
                GetText(TextId.Txt86, FormatCurrency(totalCost))
            });
        }

        private static int CalculateBillableInputTokens(RequestRecord record)
        {
            return record.CacheTokensSeparateFromInput
                ? record.PromptTokens
                : Math.Max(0, record.PromptTokens - record.CachedTokens - record.CacheCreationTokens);
        }

        private static int CalculateTotalInputTokens(RequestRecord record)
        {
            return CalculateNonCachedReadInputTokens(record) + record.CachedTokens;
        }

        private static int CalculateNonCachedReadInputTokens(RequestRecord record)
        {
            return record.CacheTokensSeparateFromInput
                ? record.PromptTokens + record.CacheCreationTokens
                : Math.Max(0, record.PromptTokens - record.CachedTokens);
        }

        private ModelCostConfig? FindModelCost(string model)
        {
            if (string.IsNullOrWhiteSpace(model))
            {
                return null;
            }

            return modelCosts.FirstOrDefault(cost => string.Equals(cost.ModelName, model, StringComparison.OrdinalIgnoreCase));
        }

        private static string FormatCurrency(decimal value)
        {
            return "$" + value.ToString("0.000000", CultureInfo.InvariantCulture);
        }

        private void UpdateTotals()
        {
            var records = statsAllDates ? LoadAllRecords() : visibleRecords;
            promptTokens = records.Sum(record => (long)CalculateTotalInputTokens(record));
            completionTokens = records.Sum(record => record.CompletionTokens);
            cachedTokens = records.Sum(record => record.CachedTokens);
            cacheCreationTokens = records.Sum(record => record.CacheCreationTokens);
            totalTokens = records.Sum(record => record.TotalTokens);
            var totalCost = records.Sum(CalculateRecordCost);

            promptTokensValueLabel.Text = promptTokens.ToString();
            completionTokensValueLabel.Text = completionTokens.ToString();
            cachedTokensValueLabel.Text = cachedTokens.ToString();
            totalCostValueLabel.Text = FormatCurrency(totalCost);
        }

        private void UpdateUsageBubble()
        {
            if (usageBubble == null)
            {
                return;
            }

            var todayRecords = LoadRecordsForDate(DateTime.Today);
            usageBubble.UpdateStats(
                todayRecords.Sum(record => (long)CalculateTotalInputTokens(record)),
                todayRecords.Sum(record => record.CompletionTokens),
                todayRecords.Sum(CalculateRecordCost));
        }

        private static string FormatCacheHitRate(long cached, long input, long cacheCreation)
        {
            var denominator = input >= cached + cacheCreation ? input : input + cached + cacheCreation;
            return denominator <= 0 ? "0.00%" : ((double)cached / denominator).ToString("P2");
        }

        private static double CalculateCacheHitRate(long cached, long input, long cacheCreation)
        {
            var denominator = input >= cached + cacheCreation ? input : input + cached + cacheCreation;
            return denominator <= 0 ? 0 : (double)cached / denominator;
        }

        private static string FormatDurationPair(long elapsedMs, long firstResponseMs)
        {
            return $"{FormatDuration(elapsedMs)}/{FormatDuration(firstResponseMs)}";
        }

        private static string FormatDuration(long milliseconds)
        {
            return milliseconds >= 1000 ? $"{milliseconds / 1000.0:0.##}s" : $"{milliseconds}ms";
        }

        private void RecordDateComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (loadingRecordDates)
            {
                return;
            }

            LoadRecordsForSelectedDate();
        }

        private void LoadRecordDates(DateTime preferredDate)
        {
            loadingRecordDates = true;
            recordDateComboBox.Items.Clear();

            var dates = Directory.GetFiles(recordsDirectory, "*.json")
                .Select(file => Path.GetFileNameWithoutExtension(file))
                .Select(name => DateTime.TryParse(name, out var date) ? date.Date : (DateTime?)null)
                .Where(date => date.HasValue)
                .Select(date => date!.Value)
                .Append(preferredDate.Date)
                .Distinct()
                .OrderByDescending(date => date)
                .ToList();

            foreach (var date in dates)
            {
                recordDateComboBox.Items.Add(date.ToString("yyyy-MM-dd"));
            }

            var selected = preferredDate.ToString("yyyy-MM-dd");
            recordDateComboBox.SelectedItem = recordDateComboBox.Items.Contains(selected) ? selected : recordDateComboBox.Items[0];
            loadingRecordDates = false;
        }

        private void AddRecordDate(DateTime date)
        {
            var text = date.ToString("yyyy-MM-dd");
            if (!recordDateComboBox.Items.Contains(text))
            {
                recordDateComboBox.Items.Insert(0, text);
            }
        }

        private DateTime GetSelectedRecordDate()
        {
            return DateTime.TryParse(recordDateComboBox.SelectedItem?.ToString(), out var date) ? date.Date : DateTime.Today;
        }

        private string GetRecordPath(DateTime date)
        {
            return Path.Combine(recordsDirectory, $"{date:yyyy-MM-dd}.json");
        }

        private void LoadRecordsForSelectedDate()
        {
            visibleRecords.Clear();
            requestGrid.Rows.Clear();

            var recordPath = GetRecordPath(GetSelectedRecordDate());
            if (File.Exists(recordPath))
            {
                try
                {
                    visibleRecords.AddRange(LoadRecordsFromPath(recordPath).OrderByDescending(record => record.Timestamp));
                }
                catch (JsonException ex)
                {
                    AppendLog(GetText(TextId.Txt87, ex.Message), true);
                }
                catch (IOException ex)
                {
                    AppendLog(GetText(TextId.Txt87, ex.Message), true);
                }
            }

            foreach (var record in visibleRecords)
            {
                AddRequestGridRow(record);
            }

            ScrollRequestGridToTop();

            UpdateTotals();
            UpdateUsageBubble();
            dailyChartPanel.Invalidate();
        }

        private List<RequestRecord> LoadRecordsForDate(DateTime date)
        {
            var recordPath = GetRecordPath(date.Date);
            if (!File.Exists(recordPath))
            {
                return new List<RequestRecord>();
            }

            try
            {
                return LoadRecordsFromPath(recordPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                AppendLog(GetText(TextId.Txt88, ex.Message), true);
                return new List<RequestRecord>();
            }
        }

        private List<RequestRecord> LoadRecordsFromPath(string recordPath)
        {
            lock (recordsLock)
            {
                return JsonSerializer.Deserialize<List<RequestRecord>>(File.ReadAllText(recordPath)) ?? new List<RequestRecord>();
            }
        }

        private void RefreshRecordGrid()
        {
            requestGrid.Rows.Clear();

            foreach (var record in visibleRecords)
            {
                AddRequestGridRow(record);
            }

            ScrollRequestGridToTop();
        }

        private void ScrollRequestGridToTop()
        {
            if (requestGrid.Rows.Count == 0)
            {
                return;
            }

            requestGrid.FirstDisplayedScrollingRowIndex = 0;
        }

        private void AddRequestGridRow(RequestRecord record)
        {
            var rowIndex = requestGrid.Rows.Add(BuildRequestGridRow(record));
            SetRequestGridRowTooltips(rowIndex, record);
        }

        private void InsertRequestGridRow(int rowIndex, RequestRecord record)
        {
            requestGrid.Rows.Insert(rowIndex, BuildRequestGridRow(record));
            SetRequestGridRowTooltips(rowIndex, record);
        }

        private void SetRequestGridRowTooltips(int rowIndex, RequestRecord record)
        {
            if (rowIndex < 0 || rowIndex >= requestGrid.Rows.Count)
            {
                return;
            }

            requestGrid.Rows[rowIndex].Cells[costColumn.Index].ToolTipText = BuildCostFormulaText(record);
        }

        private object[] BuildRequestGridRow(RequestRecord record)
        {
            return new object[]
            {
                record.Timestamp.ToString("MM-dd HH:mm"),
                record.Model,
                record.Path,
                FormatInputTokens(record),
                record.CompletionTokens,
                FormatCurrency(CalculateRecordCost(record)),
                FormatDurationPair(record.ElapsedMs, record.FirstResponseMs),
                record.StatusCode
            };
        }

        private List<RequestRecord> LoadAllRecords()
        {
            var records = new List<RequestRecord>();

            foreach (var recordPath in Directory.GetFiles(recordsDirectory, "*.json"))
            {
                try
                {
                    lock (recordsLock)
                    {
                        records.AddRange(JsonSerializer.Deserialize<List<RequestRecord>>(File.ReadAllText(recordPath)) ?? new List<RequestRecord>());
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    AppendLog(GetText(TextId.Txt89, Path.GetFileName(recordPath), ex.Message), true);
                }
            }

            return records;
        }

        private bool SaveRequestRecord(RequestRecord record)
        {
            var recordPath = GetRecordPath(record.Timestamp.Date);
            var records = new List<RequestRecord>();
            var isFirstRecordForDate = false;

            try
            {
                lock (recordsLock)
                {
                    if (File.Exists(recordPath))
                    {
                        records = JsonSerializer.Deserialize<List<RequestRecord>>(File.ReadAllText(recordPath)) ?? new List<RequestRecord>();
                    }

                    isFirstRecordForDate = records.Count == 0;
                    records.Add(record);
                    File.WriteAllText(recordPath, JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
                }

                return isFirstRecordForDate;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                AppendInternalException("Failed to save request record.", ex);
                TryBeginInvoke(() => AppendLog(GetText(TextId.Txt90, ex.Message), true));
                return false;
            }
        }

    }
}

