# Technical Specification Document: Response Panel & Output Inspector

**Application:** Fubar — Local-First, Cross-Platform HTTP Client

**Target Framework:** .NET 8 / 9 + Avalonia UI v11+

**Design System / Theme Base:** Semi.Avalonia (MIT License) with Dynamic Theme Management

**Document Version:** 1.4.0 (Response Panel, JSON Tree & Inspection Spec)

---

## 1. Executive Summary & Architectural Overview

The **Response Panel** displays execution results for outgoing HTTP requests. It provides developers with fast, accurate feedback on HTTP responses—including status indicators, latency metrics, body size, response headers, and interactive payload inspection.

### Key UX & Performance Principles

* **High-Performance JSON Tree Rendering:** Virtualized tree and code editor views capable of rendering large JSON payloads (up to 10MB+) without freezing the UI thread, utilizing `System.Text.Json` source generation and `AvaloniaEdit`.
* **At-a-Glance Telemetry:** Color-coded status badges (`200 OK`, `401 Unauthorized`, `500 Server Error`), precise latency timing (`ms`), and payload sizes (`B`, `KB`, `MB`).
* **Multi-Format Inspection:** Tabbed view modes for **Pretty JSON/XML**, **Interactive Tree**, **Raw Text**, **Response Headers**, and **Visual Preview** (Images/HTML).
* **Deep In-Response Search & JSONPath Filtering:** Real-time text search and JSONPath evaluation (`$.data.users[0].id`) with match highlighting and rapid navigation.

---

## 2. Layout Schematic & Grid Breakdown

The Response Panel can be arranged either below the Request Editor (vertical split) or to its right (horizontal split) using a flexible Avalonia `GridSplitter`.

```
+-----------------------------------------------------------------------------------------+
| 1. TELEMETRY & HEADER TOOLBAR                                                           |
|   [ 200 OK ]  ⏱️ 142 ms  📦 12.4 KB  │  [ Pretty ] [ Tree ] [ Raw ] [ Headers (8) ]  │ 🔍|
+-----------------------------------------------------------------------------------------+
| 2. IN-RESPONSE SEARCH & JSONPATH FILTER BAR (Expandable / Ctrl+F)                       |
|   [ 🔍 Find in response or JSONPath (e.g. $.items[0].name)... ]  [ 1/14 ] [ ⬆ ] [ ⬇ ]  |
+-----------------------------------------------------------------------------------------+
| 3. MAIN RESPONSE VIEWER AREA (Flexible Height / Scrollable / Virtualized)              |
|                                                                                         |
|   ▼ {                                                                                   |
|     "status": "success",                                                                |
|   ▼ "data": {                                                                           |
|       "total": 142,                                                                     |
|     ▼ "users": [                                                                        |
|         ▼ { "id": 101, "name": "Alice", "role": "admin" },                              |
|           { "id": 102, "name": "Bob",   "role": "developer" }                         |
|       ]                                                                                 |
|     }                                                                                   |
|   }                                                                                     |
+-----------------------------------------------------------------------------------------+
| 4. ACTION FOOTER & CONTENT METADATA                                                     |
|   ContentType: application/json; charset=utf-8   │   [ 📋 Copy Body ] [ 💾 Save File ]    |
+-----------------------------------------------------------------------------------------+

```

---

## 3. Status Badges & Telemetry Bar Specification

Anchored at the top of the Response Panel, the telemetry bar gives immediate status feedback.

```
+-----------------------------------------------------------------------------------------+
| [ 200 OK ]  ⏱️ 118 ms  📦 4.2 KB  │  [ Pretty ]  [ Tree ]  [ Raw ]  [ Headers (6) ]     |
+-----------------------------------------------------------------------------------------+

```

### 3.1 HTTP Status Code Formatting Rules

| Status Range | Badge Background (Dark / Light) | Text / Border | Icon / Symbol |
| --- | --- | --- | --- |
| **1xx Informational** | `#1E293B` / `#F1F5F9` | `#94A3B8` / `#475569` | `ℹ️` Info |
| **2xx Success** | `#14532D` / `#DCFCE7` | `#4ADE80` / `#15803D` | `✅` Checkmark |
| **3xx Redirection** | `#1E3A8A` / `#E0F2FE` | `#60A5FA` / `#0369A1` | `↪️` Arrow |
| **4xx Client Error** | `#7C2D12` / `#FFEDD5` | `#FB923C` / `#C2410C` | `⚠️` Warning |
| **5xx Server Error** | `#7F1D1D` / `#FEE2E2` | `#F87171` / `#B91C1C` | `❌` Danger |
| **Connection Failed** | `#3F3F46` / `#E4E4E7` | `#A1A1AA` / `#52525B` | `🔌` Disconnected |

### 3.2 Performance & Size Badges

* **Latency Badge (`⏱️ Time`)**: Formatted dynamically in `ms` or `s` (e.g., `42 ms`, `1.24 s`). Color shifts to yellow if > 1000ms, and red if > 5000ms.
* **Payload Size Badge (`📦 Size`)**: Formatted as `B`, `KB`, or `MB` (e.g., `850 B`, `14.2 KB`, `3.1 MB`).

---

## 4. Multi-View Output Engine

Developers can toggle between five distinct response views depending on the content type received.

```
+-----------------------------------------------------------------------------------------+
| VIEW MODE SELECTOR:                                                                     |
| [ ⚡ Pretty ]  [ 🌳 Interactive Tree ]  [ 📄 Raw ]  [ 📋 Headers (12) ]  [ 🖼️ Preview ]   |
+-----------------------------------------------------------------------------------------+

```

### 4.1 View Mode Breakdown

#### A. Pretty View (`AvaloniaEdit` Read-Only Code Editor)

* **Syntax Highlighting**: Custom colorizer for JSON, XML, HTML, and SQL payloads.
* **Line Numbers & Folding**: Code folding collapse toggles (`▼` / `▶`) on objects and arrays.
* **Auto-Formatting**: Automatically indents raw unformatted JSON or minified XML responses.

#### B. Interactive Tree View (`TreeView` Virtualized)

* **Expand / Collapse All**: Toolbar triggers to expand or collapse deep node levels.
* **Node Context Menu**:
* **`Copy Path`**: Copies JSONPath (e.g., `$.data.users[0].email`).
* **`Copy Value`**: Copies raw node value.
* **`Copy Node as JSON`**: Copies selected object/array subtree.


* **Type Icons**: Micro-indicators for data types: `{}` Object, `[]` Array, `" "` String, `#` Number, `true/false` Boolean, `null` Null.

#### C. Raw View (`TextBox` Unformatted Monospace)

* Direct stream display without parsing overhead. Recommended for huge files (> 10MB) or raw binary inspection.

#### D. Headers Inspector (`DataGrid`)

* Renders response headers in a two-column searchable table (`Header Key` | `Header Value`).
* Copy individual header values or copy all as HTTP header string format.

#### E. Visual Preview Mode (Conditional)

* **Images**: Renders `image/png`, `image/jpeg`, `image/svg+xml`, `image/webp` directly in an image canvas.
* **HTML Render**: Renders HTML responses inside a lightweight web container or sanitized preview card.

---

## 5. In-Response Search & JSONPath Filtering System

Pressing `Ctrl+F` while focused on the response pane reveals the sticky **Response Filter Toolbar**.

```
+-----------------------------------------------------------------------------------------+
| [ 🔍 $.data.users[?(@.role=='admin')].name                   ]  [ 3 matches ]  [ ⬆ ] [ ⬇ ]|
+-----------------------------------------------------------------------------------------+

```

### Search Engine Capabilities

1. **Plain Text Search**: Standard substring match across keys and values. Matches are highlighted in yellow (`#F59E0B`).
2. **Regex Search**: Toggled via `[ .* ]` button for regular expression matches.
3. **JSONPath Filter Engine**:
* Evaluates expressions against JSON payloads using `Newtonsoft.Json.Linq` or `System.Text.Json.Nodes`.
* **Live Filtering Mode**: Filters the tree/pretty view to display **only nodes that match the query** (e.g., `$.items[*].id`).
* **Error Handling**: Displays an inline warning indicator if the JSONPath syntax is invalid.



---

## 6. Design System Tokens & Dual-Theme Palette

| Token Key | Dark Mode Hex | Light Mode Hex | Purpose / Usage |
| --- | --- | --- | --- |
| `BgResponsePanel` | `#141416` | `#F8F9FA` | Response Panel container background |
| `BgResponseHeader` | `#1E1E22` | `#FFFFFF` | Telemetry header and view mode tab bar |
| `JsonKey` | `#93C5FD` | `#0284C7` | JSON object keys (`"id":`) |
| `JsonString` | `#4ADE80` | `#16A34A` | JSON string values (`"Alice"`) |
| `JsonNumber` | `#F6AD55` | `#D97706` | JSON numeric values (`101`, `3.14`) |
| `JsonBoolean` | `#F472B6` | `#C026D3` | JSON boolean values (`true`, `false`) |
| `JsonNull` | `#A1A1AA` | `#64748B` | JSON null values (`null`) |
| `SearchHighlight` | `#F59E0B` | `#FDE047` | Matched search term background highlight |

---

## 7. Avalonia XAML Blueprint: Response Panel View

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Fubar.ViewModels"
             xmlns:AvaloniaEdit="clr-namespace:AvaloniaEdit;assembly=AvaloniaEdit"
             x:Class="Fubar.Views.ResponsePanelView"
             x:DataType="vm:ResponsePanelViewModel">

  <Grid RowDefinitions="Auto, Auto, *, Auto" Background="{DynamicResource BgResponsePanel}">

    <!-- ============================================================ -->
    <!-- 1. TELEMETRY & HEADER TOOLBAR                               -->
    <!-- ============================================================ -->
    <Border Grid.Row="0" Background="{DynamicResource BgResponseHeader}" Padding="8,6" BorderBrush="{DynamicResource BorderSubtle}" BorderThickness="0,0,0,1">
      <Grid ColumnDefinitions="Auto, Auto, Auto, *, Auto">
        
        <!-- Status Badge -->
        <Border Grid.Column="0" 
                Background="{Binding StatusBadgeBackground}" 
                CornerRadius="4" 
                Padding="8,3" 
                Margin="0,0,8,0">
          <StackPanel Orientation="Horizontal" Spacing="4">
            <TextBlock Text="{Binding StatusIcon}" FontSize="12"/>
            <TextBlock Text="{Binding StatusCodeText}" FontWeight="Bold" Foreground="{Binding StatusBadgeForeground}" FontSize="12"/>
          </StackPanel>
        </Border>

        <!-- Latency / Duration Badge -->
        <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="4" VerticalAlignment="Center" Margin="0,0,12,0">
          <TextBlock Text="⏱️" FontSize="12"/>
          <TextBlock Text="{Binding ElapsedTimeText}" FontSize="12" Foreground="{DynamicResource TextPrimary}" FontFamily="Cascadia Code, Consolas, monospace"/>
        </StackPanel>

        <!-- Size Badge -->
        <StackPanel Grid.Column="2" Orientation="Horizontal" Spacing="4" VerticalAlignment="Center" Margin="0,0,16,0">
          <TextBlock Text="📦" FontSize="12"/>
          <TextBlock Text="{Binding ContentSizeText}" FontSize="12" Foreground="{DynamicResource TextPrimary}" FontFamily="Cascadia Code, Consolas, monospace"/>
        </StackPanel>

        <!-- View Mode Segmented Control -->
        <StackPanel Grid.Column="4" Orientation="Horizontal" Spacing="2">
          <RadioButton Classes="TabPill" Content="Pretty" IsChecked="{Binding IsPrettyViewSelected}"/>
          <RadioButton Classes="TabPill" Content="Tree" IsChecked="{Binding IsTreeViewSelected}"/>
          <RadioButton Classes="TabPill" Content="Raw" IsChecked="{Binding IsRawViewSelected}"/>
          <RadioButton Classes="TabPill" Content="{Binding HeadersTabTitle}" IsChecked="{Binding IsHeadersViewSelected}"/>
          <RadioButton Classes="TabPill" Content="Preview" IsChecked="{Binding IsPreviewViewSelected}" IsVisible="{Binding HasPreview}"/>
        </StackPanel>

      </Grid>
    </Border>

    <!-- ============================================================ -->
    <!-- 2. IN-RESPONSE SEARCH & JSONPATH FILTER BAR                  -->
    <!-- ============================================================ -->
    <Border Grid.Row="1" 
            IsVisible="{Binding IsSearchOpen}" 
            Background="{DynamicResource BgHeader}" 
            Padding="8,4" 
            BorderBrush="{DynamicResource BorderSubtle}" 
            BorderThickness="0,0,0,1">
      <Grid ColumnDefinitions="*, Auto, Auto, Auto, Auto">
        <TextBox Grid.Column="0" 
                 Text="{Binding SearchQuery, Mode=TwoWay}" 
                 Watermark="Search response or enter JSONPath (e.g. $.data.items[0])..." 
                 Height="28" 
                 FontSize="12"
                 FontFamily="Cascadia Code, Consolas, monospace"
                 VerticalContentAlignment="Center"/>

        <TextBlock Grid.Column="1" 
                   Text="{Binding SearchMatchCountText}" 
                   Margin="8,0" 
                   VerticalAlignment="Center" 
                   FontSize="11" 
                   Foreground="{DynamicResource TextSecondary}"/>

        <Button Grid.Column="2" Command="{Binding PreviousMatchCommand}" Content="⬆" Width="26" Height="26" Margin="2,0" Classes="Flat"/>
        <Button Grid.Column="3" Command="{Binding NextMatchCommand}" Content="⬇" Width="26" Height="26" Margin="2,0" Classes="Flat"/>
        <Button Grid.Column="4" Command="{Binding CloseSearchCommand}" Content="✕" Width="26" Height="26" Margin="2,0" Classes="Flat"/>
      </Grid>
    </Border>

    <!-- ============================================================ -->
    <!-- 3. MAIN RESPONSE CONTENT DISPLAY AREA                       -->
    <!-- ============================================================ -->
    <Grid Grid.Row="2">
      
      <!-- PRETTY CODE VIEW (AvaloniaEdit) -->
      <AvaloniaEdit:TextEditor IsVisible="{Binding IsPrettyViewSelected}"
                               Document="{Binding ResponseTextDocument}"
                               IsReadOnly="True"
                               ShowLineNumbers="True"
                               FontFamily="Cascadia Code, Consolas, monospace"
                               FontSize="13"
                               Background="{DynamicResource BgResponsePanel}"
                               Foreground="{DynamicResource TextPrimary}"
                               Padding="8"/>

      <!-- INTERACTIVE TREE VIEW -->
      <TreeView IsVisible="{Binding IsTreeViewSelected}"
                ItemsSource="{Binding JsonTreeNodes}"
                Background="{DynamicResource BgResponsePanel}"
                Padding="8">
        <TreeView.ItemTemplate>
          <TreeDataTemplate ItemsSource="{Binding Children}">
            <StackPanel Orientation="Horizontal" Spacing="6" Height="22">
              <TextBlock Text="{Binding Key}" Foreground="{DynamicResource JsonKey}" FontFamily="Cascadia Code, Consolas, monospace"/>
              <TextBlock Text=":" Foreground="{DynamicResource TextSecondary}"/>
              <TextBlock Text="{Binding DisplayValue}" Foreground="{Binding ValueBrush}" FontFamily="Cascadia Code, Consolas, monospace"/>
              <TextBlock Text="{Binding TypeSummary}" Foreground="{DynamicResource TextSecondary}" FontSize="10" VerticalAlignment="Center"/>
            </StackPanel>
          </TreeDataTemplate>
        </TreeView.ItemTemplate>
      </TreeView>

      <!-- HEADERS DATA GRID VIEW -->
      <DataGrid IsVisible="{Binding IsHeadersViewSelected}"
                ItemsSource="{Binding ResponseHeaders}"
                AutoGenerateColumns="False"
                GridLinesVisibility="Horizontal"
                HeadersVisibility="Column"
                Padding="8">
        <DataGrid.Columns>
          <DataGridTextColumn Header="Header Name" Binding="{Binding Key}" Width="1*"/>
          <DataGridTextColumn Header="Header Value" Binding="{Binding Value}" Width="2*"/>
        </DataGrid.Columns>
      </DataGrid>

      <!-- PREVIEW IMAGE VIEW -->
      <ScrollViewer IsVisible="{Binding IsPreviewViewSelected}">
        <Image Source="{Binding PreviewBitmapImage}" Stretch="None" HorizontalAlignment="Center" VerticalAlignment="Center" Margin="16"/>
      </ScrollViewer>

    </Grid>

    <!-- ============================================================ -->
    <!-- 4. ACTION FOOTER & METADATA                                  -->
    <!-- ============================================================ -->
    <Border Grid.Row="3" Background="{DynamicResource BgResponseHeader}" Padding="8,4" BorderBrush="{DynamicResource BorderSubtle}" BorderThickness="0,1,0,0">
      <Grid ColumnDefinitions="*, Auto">
        <TextBlock Grid.Column="0" 
                   Text="{Binding ContentTypeHeader}" 
                   FontSize="11" 
                   Foreground="{DynamicResource TextSecondary}" 
                   VerticalAlignment="Center"
                   FontFamily="Cascadia Code, Consolas, monospace"/>

        <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="6">
          <Button Content="📋 Copy Body" Command="{Binding CopyResponseBodyCommand}" Classes="Outline"/>
          <Button Content="💾 Save File" Command="{Binding SaveResponseToFileCommand}" Classes="Outline"/>
        </StackPanel>
      </Grid>
    </Border>

  </Grid>
</UserControl>

```

---

## 8. ViewModel Implementation Blueprint

```csharp
using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Media;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Fubar.ViewModels;

public partial class ResponsePanelViewModel : ObservableObject
{
    [ObservableProperty]
    private int _statusCode = 200;

    [ObservableProperty]
    private string _statusCodeText = "200 OK";

    [ObservableProperty]
    private string _statusIcon = "✅";

    [ObservableProperty]
    private string _elapsedTimeText = "0 ms";

    [ObservableProperty]
    private string _contentSizeText = "0 B";

    [ObservableProperty]
    private string _contentTypeHeader = "application/json";

    [ObservableProperty]
    private bool _isPrettyViewSelected = true;

    [ObservableProperty]
    private bool _isTreeViewSelected;

    [ObservableProperty]
    private bool _isHeadersViewSelected;

    [ObservableProperty]
    private bool _isSearchOpen;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private TextDocument _responseTextDocument = new();

    public ObservableCollection<HeaderRowViewModel> ResponseHeaders { get; } = new();
    public ObservableCollection<JsonTreeNodeViewModel> JsonTreeNodes { get; } = new();

    [RelayCommand]
    public void LoadResponse(HttpResponseMessage response, byte[] bodyBytes, long elapsedMs)
    {
        StatusCode = (int)response.StatusCode;
        StatusCodeText = $"{StatusCode} {response.ReasonPhrase}";
        ElapsedTimeText = $"{elapsedMs} ms";
        ContentSizeText = FormatFileSize(bodyBytes.Length);
        
        // Update Status Badge Styling & Icons
        UpdateStatusBadgeVisuals(StatusCode);

        // Populate Headers
        ResponseHeaders.Clear();
        foreach (var header in response.Headers.Concat(response.Content.Headers))
        {
            ResponseHeaders.Add(new HeaderRowViewModel(header.Key, string.Join(", ", header.Value)));
        }

        // Format Payload Body
        var rawText = System.Text.Encoding.UTF8.GetString(bodyBytes);
        if (TryFormatJson(rawText, out var formattedJson))
        {
            ResponseTextDocument.Text = formattedJson;
            BuildJsonTreeNodes(formattedJson);
        }
        else
        {
            ResponseTextDocument.Text = rawText;
        }
    }

    private bool TryFormatJson(string input, out string formatted)
    {
        try
        {
            using var doc = JsonDocument.Parse(input);
            formatted = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
            return true;
        }
        catch
        {
            formatted = input;
            return false;
        }
    }

    private void UpdateStatusBadgeVisuals(int code)
    {
        StatusIcon = code switch
        {
            >= 200 and < 300 => "✅",
            >= 300 and < 400 => "↪️",
            >= 400 and < 500 => "⚠️",
            >= 500 => "❌",
            _ => "ℹ️"
        };
    }

    private static string FormatFileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F2} MB"
    };

    private void BuildJsonTreeNodes(string json)
    {
        JsonTreeNodes.Clear();
        // Parse and populate hierarchical JsonTreeNodeViewModel collection
    }
}

```