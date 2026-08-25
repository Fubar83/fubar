# Technical Specification Document: Central Request Editor Pane

**Application:** Fubar — Local-First, Cross-Platform HTTP Client

**Target Framework:** .NET 8 / 9 + Avalonia UI v11+

**Design System / Theme Base:** Semi.Avalonia (MIT License) with Dynamic Theme Management

**Document Version:** 1.3.0 (Single-Request Canvas, Dynamic Sync, Variable Tooltips & History Replay)

---

## 1. Executive Summary & Revised Architectural Rules

Version 1.3.0 streamables the Central Request Editor to focus strictly on a **Single Active Request Canvas** (eliminating multi-tab document overhead), while establishing robust parameter synchronization, inheritance cascades, and universal variable resolution.

### Core Revisions

1. **Single Active Request Canvas:** No request tabs. Clicking a request in the Left Pane Explorer loads it directly into the single main editor pane.
2. **Bi-Directional Query Sync:** The address bar URL and the Query Params editor are bound bidirectionally. Toggling a parameter off strips it from the live URL while preserving its definition in the editor.
3. **Environment-Only Variables:** Request-scoped local variables are removed. All `{{variable}}` tokens resolve strictly from the active **Environment**.
4. **Header & Auth Inheritance:** Headers inherit down from parent folders and Auth Profiles. Inherited headers render **readonly** and **grayed out**, but remain **toggleable on/off**.
5. **Universal Variable Styling & Tooltips:** `{{variable}}` tokens in URLs, headers, and bodies feature visual status colors (valid vs. misspelled/undefined) and reveal resolved values on hover.
6. **History Snapshot Replay:** Historical executions can be replayed directly, storing the outcome as a brand-new execution snapshot in the history log.

---

## 2. Layout Schematic & Grid Breakdown

The Central Request Canvas consists of three vertical regions inside a clean, high-density layout:

```
+-----------------------------------------------------------------------------------------+
| 1. ADDRESS BAR & EXECUTION CONTROL                                                       |
|   [ GET  ▼ ]  https://api.fubar.dev/v1/users?limit=10&page={{currPage}}  [ 🚀 Send ▼ ]  |
+-----------------------------------------------------------------------------------------+
| 2. REQUEST PARAMETER TAB NAV                                                            |
|   [ Params (2) ]  [ Headers (4) ]  [ Body (JSON) ]  [ Auth 🔒 ]  [ 🕒 History ]             |
+-----------------------------------------------------------------------------------------+
| 3. ACTIVE EDITOR CONTENT AREA                                                            |
|                                                                                         |
|   ┌── Headers Tab Example (Showing Inheritance & Variables) ────────────────────────┐   |
|   │ En  Key                    Value                   Source        Actions            │   |
|   │ ─────────────────────────────────────────────────────────────────────────── │   |
|   │ [x] Authorization          Bearer {{adminToken}}   [🔒 Auth Profile] (Readonly)     │   |
|   │ [x] X-Workspace-Id         {{workspaceGuid}}       [📁 Folder]       (Readonly)     │   |
|   │ [x] Content-Type           application/json        [Direct]      [ 🗑️ ]             │   |
|   │ [ ] {{customHeaderName}}   {{customHeaderVal}}     [Direct]      [ 🗑️ ]             │   |
|   └────────────────────────────────────────────────────────────────────────────┘   |
+-----------------------------------------------------------------------------------------+

```

---

## 3. Dynamic URL & Parameter Synchronization Engine

The URL input box and the `Params` tab are synchronized in real-time through a dedicated URL parser view model.

```
                  ┌─────────────────────────────────────────┐
                  │            Address Bar URL              │
                  │  https://api.com/users?page=1&limit=10  │
                  └────────────────────┬────────────────────┘
                                       │
                         Bidirectional Sync Engine
                                       │
                  ┌────────────────────┴────────────────────┐
                  │             Params Table                │
                  │  [x] page  = 1                          │
                  │  [x] limit = 10                         │
                  │  [ ] debug = true (Stripped from URL)   │
                  └─────────────────────────────────────────┘

```

### Sync Rules & Behaviors

* **URL to Table Sync:** Typing query string pairs (e.g., `?search=fubar&sort=asc`) into the URL bar instantly updates or appends corresponding rows in the `Params` table.
* **Table to URL Sync:** Modifying a Key or Value in the table immediately recalculates and updates the address bar query string.
* **Toggle Suppression:** Unchecking a parameter `[ ]` removes it from the address bar URL immediately. Re-checking it `[x]` appends it back to the URL without losing its stored key/value.

---

## 4. Universal Variable Resolution & Tooltip System

All text inputs across the request editor (URL bar, Header Keys, Header Values, Params, JSON Body) evaluate variable tokens using the `{{variableName}}` syntax.

```
                    ┌───────────────────────────────────────┐
                    │  https://api.dev/v1/{{entity}}/{{id}} │
                    └───────────────────┬───────────────────┘
                                        │
                         Hovering over {{entity}}
                                        │
                    ┌───────────────────▼───────────────────┐
                    │  Resolved: "users"                    │
                    │  Source: Staging Environment          │
                    └───────────────────────────────────────┘

```

### 4.1 Visual Color States

| Variable State | Visual Rendering | Hover Tooltip Display |
| --- | --- | --- |
| **Defined / Valid** | **Blue Accent Pill / Text** (`#3B82F6` Dark / `#2563EB` Light) | **Resolved Value:** `users`**Scope:** `Staging Environment` |
| **Undefined / Misspelled** | **Amber Warning Underline / Pill** (`#F59E0B` Dark / `#D97706` Light) | **⚠️ Undefined Variable:** `{{entty}}`*Not found in active environment.* |

---

## 5. Header & Auth Inheritance Model

Requests inherit headers from two parent sources:

1. **Folder Ancestry:** Headers defined at the workspace root or parent collections.
2. **Auth Profiles:** Headers generated by the assigned Auth Profile (e.g., `Authorization: Bearer {{token}}`).

```
+-------------------------------------------------------------------------------------------------+
| EN | KEY                   | VALUE                  | SOURCE / ORIGIN       | ACTIONS           |
+----+-----------------------+------------------------+-----------------------+-------------------+
| [x]| Authorization         | Bearer {{oauthToken}}  | 🔒 Auth: Admin Profile| (Readonly / Gray) |
| [x]| X-Tenant-Id           | {{tenantId}}           | 📁 Folder: Root       | (Readonly / Gray) |
| [x]| Content-Type          | application/json       | 📄 Request Direct     | [ 🗑️ Delete ]      |
| [ ]| {{customHeaderKey}}   | {{customHeaderVal}}    | 📄 Request Direct     | [ 🗑️ Delete ]      |
+-------------------------------------------------------------------------------------------------+

```

### Inheritance Rules

* **Visual Cue:** Inherited header rows render with a muted gray background (`#1E1E22` Dark / `#F1F5F9` Light) and locked Key/Value text fields.
* **Toggleable Override:** Developers can uncheck `[ ]` an inherited header to temporarily suppress it from being transmitted with the outgoing HTTP request.
* **Variable Parsing in Headers:** Variables syntax `{{variable}}` is supported in **both Key and Value** fields for direct headers and inherited headers alike.

---

## 6. History Tab & Execution Replay Engine

The `🕒 History` tab stores a chronological ledger of previous runs for the selected request.

```
+-----------------------------------------------------------------------------------------+
| REQUEST EXECUTION HISTORY                                                               |
| ─────────────────────────────────────────────────────────────────────────────────────── |
|  ● 2026-08-13 14:22  │  200 OK          │  118 ms  │  1.4 KB  │  [ ⚡ Replay Execution ]|
|  ● 2026-08-13 14:15  │  401 Unauthorized │   45 ms  │  320 B   │  [ ⚡ Replay Execution ]|
|  ● 2026-08-13 12:05  │  500 Internal Err │  850 ms  │  2.1 KB  │  [ ⚡ Replay Execution ]|
+-----------------------------------------------------------------------------------------+

```

### Replay Workflow

1. Clicking **`⚡ Replay Execution`** reads the exact request snapshot payload (URL, headers, body) captured at that historical moment.
2. The engine executes the request again using current environment variable resolutions.
3. The new result is recorded as a **brand-new entry** at the top of the history timeline without overwriting past entries.

---

## 7. Avalonia XAML Blueprint: Refined Single-Request Canvas

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Fubar.ViewModels"
             x:Class="Fubar.Views.RequestEditorView"
             x:DataType="vm:RequestEditorViewModel">

  <Grid RowDefinitions="Auto, Auto, *">

    <!-- ============================================================ -->
    <!-- 1. ADDRESS BAR & EXECUTION CONTROL                           -->
    <!-- ============================================================ -->
    <Border Grid.Row="0" Padding="12" Background="{DynamicResource BgEditorCanvas}">
      <Grid ColumnDefinitions="Auto, *, Auto, Auto">
        
        <!-- Method Selector -->
        <ComboBox Grid.Column="0" 
                  Width="110" 
                  Height="36"
                  ItemsSource="{Binding AvailableMethods}" 
                  SelectedItem="{Binding SelectedMethod}"
                  CornerRadius="6,0,0,6"
                  BorderBrush="{DynamicResource BorderEditor}"/>

        <!-- URL Input Field with Dynamic Variable Tooltips -->
        <TextBox Grid.Column="1" 
                 Height="36" 
                 Text="{Binding RequestUrl, Mode=TwoWay}"
                 Watermark="https://api.example.com/v1/resource?param={{var}}"
                 CornerRadius="0"
                 BorderBrush="{DynamicResource BorderEditor}"
                 BorderThickness="0,1,1,1"
                 FontFamily="Cascadia Code, Consolas, monospace"
                 FontSize="13"
                 VerticalContentAlignment="Center"/>

        <!-- Send Execution Button -->
        <Button Grid.Column="2" 
                Height="36" 
                Margin="8,0,0,0" 
                Command="{Binding SendRequestCommand}"
                Background="{DynamicResource BtnSendBg}"
                Foreground="White"
                CornerRadius="6">
          <StackPanel Orientation="Horizontal" Spacing="6" Padding="12,0">
            <TextBlock Text="🚀 Send" FontWeight="SemiBold"/>
          </StackPanel>
        </Button>

        <!-- Save Button -->
        <Button Grid.Column="3" 
                Height="36" 
                Width="36" 
                Margin="6,0,0,0" 
                Command="{Binding SaveRequestCommand}"
                ToolTip.Tip="Save Request (Ctrl+S)"
                CornerRadius="6"
                BorderBrush="{DynamicResource BorderEditor}">
          <TextBlock Text="💾" FontSize="14" HorizontalAlignment="Center"/>
        </Button>
      </Grid>
    </Border>

    <!-- ============================================================ -->
    <!-- 2. PARAMETER TAB NAVIGATION (NO REQUEST TABS)                -->
    <!-- ============================================================ -->
    <Border Grid.Row="1" Background="{DynamicResource BgEditorCanvas}" Padding="12,0" BorderBrush="{DynamicResource BorderEditor}" BorderThickness="0,0,0,1">
      <StackPanel Orientation="Horizontal" Spacing="4">
        <RadioButton Classes="TabPill" Content="Params" IsChecked="{Binding IsParamsSelected}" />
        <RadioButton Classes="TabPill" Content="Headers" IsChecked="{Binding IsHeadersSelected}" />
        <RadioButton Classes="TabPill" Content="Body" IsChecked="{Binding IsBodySelected}" />
        <RadioButton Classes="TabPill" Content="Auth" IsChecked="{Binding IsAuthSelected}" />
        <RadioButton Classes="TabPill" Content="🕒 History" IsChecked="{Binding IsHistorySelected}" />
      </StackPanel>
    </Border>

    <!-- ============================================================ -->
    <!-- 3. EDITOR CONTENT AREA                                       -->
    <!-- ============================================================ -->
    <Grid Grid.Row="2" Background="{DynamicResource BgEditorCanvas}" Padding="12">
      
      <!-- HEADERS EDITOR VIEW WITH INHERITANCE SUPPORT -->
      <DataGrid IsVisible="{Binding IsHeadersSelected}"
                ItemsSource="{Binding CombinedHeaders}"
                AutoGenerateColumns="False"
                GridLinesVisibility="Horizontal"
                HeadersVisibility="Column"
                RowHeight="32">
        <DataGrid.Columns>
          <!-- Enable Checkbox (Toggleable for ALL headers, including inherited) -->
          <DataGridCheckBoxColumn Header="Enable" Binding="{Binding IsEnabled}" Width="60"/>
          
          <!-- Header Key -->
          <DataGridTemplateColumn Header="Key" Width="1*">
            <DataGridTemplateColumn.CellTemplate>
              <DataTemplate>
                <TextBlock Text="{Binding Key}" 
                           IsEnabled="{Binding !IsInherited}"
                           Opacity="{Binding IsInherited, Converter={StaticResource InheritedOpacityConverter}}"
                           VerticalAlignment="Center" 
                           Padding="6,0"/>
              </DataTemplate>
            </DataGridTemplateColumn.CellTemplate>
          </DataGridTemplateColumn>

          <!-- Header Value (Supports {{variable}} ) -->
          <DataGridTemplateColumn Header="Value" Width="2*">
            <DataGridTemplateColumn.CellTemplate>
              <DataTemplate>
                <TextBlock Text="{Binding Value}" 
                           IsEnabled="{Binding !IsInherited}"
                           Opacity="{Binding IsInherited, Converter={StaticResource InheritedOpacityConverter}}"
                           VerticalAlignment="Center" 
                           Padding="6,0"/>
              </DataTemplate>
            </DataGridTemplateColumn.CellTemplate>
          </DataGridTemplateColumn>

          <!-- Source Origin Tag -->
          <DataGridTextColumn Header="Source" Binding="{Binding SourceName}" IsReadOnly="True" Width="140"/>
        </DataGrid.Columns>
      </DataGrid>

      <!-- AUTH SELECTION TAB -->
      <StackPanel IsVisible="{Binding IsAuthSelected}" Spacing="16" MaxWidth="500" HorizontalAlignment="Left">
        <TextBlock Text="Authentication Configuration" FontSize="16" FontWeight="SemiBold"/>
        <TextBlock Text="Select an existing Auth Profile from the workspace library, inherit from the parent folder, or explicitly disable authentication." TextWrapping="Wrap" Foreground="{DynamicResource TextSecondary}"/>
        
        <ComboBox ItemsSource="{Binding AvailableAuthProfiles}" 
                  SelectedItem="{Binding SelectedAuthProfile}" 
                  HorizontalAlignment="Stretch">
          <!-- Options: Inherit (Parent Profile Name), None, [List of Auth Profiles] -->
        </ComboBox>
      </StackPanel>

      <!-- HISTORY TAB WITH REPLAY ACTION -->
      <DataGrid IsVisible="{Binding IsHistorySelected}"
                ItemsSource="{Binding ExecutionHistory}"
                AutoGenerateColumns="False"
                GridLinesVisibility="Horizontal"
                HeadersVisibility="Column">
        <DataGrid.Columns>
          <DataGridTextColumn Header="Timestamp" Binding="{Binding Timestamp}" Width="160"/>
          <DataGridTextColumn Header="Status" Binding="{Binding StatusCode}" Width="100"/>
          <DataGridTextColumn Header="Duration" Binding="{Binding DurationMs, StringFormat='{}{0} ms'}" Width="100"/>
          <DataGridTemplateColumn Header="Action" Width="140">
            <DataGridTemplateColumn.CellTemplate>
              <DataTemplate>
                <Button Content="⚡ Replay" 
                        Command="{Binding $parent[DataGrid].((vm:RequestEditorViewModel)DataContext).ReplayHistoryCommand}"
                        CommandParameter="{Binding}"
                        Classes="Outline"/>
              </DataTemplate>
            </DataGridTemplateColumn.CellTemplate>
          </DataGridTemplateColumn>
        </DataGrid.Columns>
      </DataGrid>

    </Grid>
  </Grid>
</UserControl>

```