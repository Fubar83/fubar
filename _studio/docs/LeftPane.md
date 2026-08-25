# Updated Technical Specification Document: Left Navigation Pane

**Application:** Fubar — Local-First, Cross-Platform HTTP Client

**Target Framework:** .NET 10 (latest LTS) + Avalonia UI v11+ (latest)

**Design System / Theme Base:** Semi.Avalonia (MIT License) with Dynamic Theme Management

**Document Version:** 1.0.0

---

## 1. Executive Summary & Aesthetic Objectives

The Left Navigation Pane serves as the central command hub for **Fubar**. It manages multi-workspace context, active environments, auth credential profiles, and workspace folder hierarchies.

### Visual & UX Principles

* **Adaptive Theme Support:** First-class support for **Dark Mode**, **Light Mode**, and **System Default (Auto)** theme syncing without requiring application restarts.
* **Developer-Focused Density:** High visual density with tight padding (4px–8px) to maximize visible hierarchy depth across light and dark backgrounds.
* **Semantic Token Architecture:** All visual components utilize dynamic semantic resource tokens (`DynamicResource`), ensuring seamless runtime theme switches.
* **Instant Visual Scanning:** Color-coded method badges, status indicators, and subtle hierarchy indentations engineered for high contrast in all themes.

---

## 2. Theme Engine & Style Architecture

Fubar utilizes Avalonia 11’s native `RequestedThemeVariant` engine coupled with `ResourceDictionary.ThemeDictionaries`. This ensures crisp contrast and native platform feel across Windows (Fluent), macOS (Aqua/Mica), and Linux.

```
                  ┌────────────────────────────────────────┐
                  │      Theme Variant Selector (UI)       │
                  │   [ 🌙 Dark | ☀️ Light | 💻 System ]   │
                  └──────────────────┬─────────────────────┘
                                     │
                                     ▼
                  ┌────────────────────────────────────────┐
                  │    Application.RequestedThemeVariant   │
                  └──────────────────┬─────────────────────┘
                                     │
             ┌───────────────────────┴───────────────────────┐
             ▼                                               ▼
┌─────────────────────────┐                     ┌─────────────────────────┐
│ ThemeVariant.Dark       │                     │ ThemeVariant.Light      │
│ Semi.Avalonia Dark Base │                     │ Semi.Avalonia Light Base│
└────────────┬────────────┘                     └────────────┬────────────┘
             │                                               │
             └───────────────────────┬───────────────────────┘
                                     │
                                     ▼
                  ┌────────────────────────────────────────┐
                  │      DynamicResource Token Lookup      │
                  │ (BgSidebar, TextPrimary, BorderSubtle) │
                  └────────────────────────────────────────┘

```

---

## 3. Design System Tokens & Dual-Theme Palette

The table below maps semantic resource tokens to their specific visual implementations in both Dark and Light variants.

| Token Key | Dark Mode Hex | Light Mode Hex | Purpose / Usage |
| --- | --- | --- | --- |
| `BgSidebar` | `#141416` | `#F8F9FA` | Left pane main background |
| `BgHeader` | `#1E1E22` | `#FFFFFF` | Header cards, inputs, dropdown backgrounds |
| `BgHover` | `#282830` | `#EAECEF` | Hover state over tree rows and buttons |
| `BgSelected` | `#2D323E` | `#DCE3F0` | Active selected tree node background |
| `BorderSubtle` | `#2B2B32` | `#E1E4E8` | Section dividers and control outlines |
| `TextPrimary` | `#ECECEF` | `#1F2328` | Main item titles, request names |
| `TextSecondary` | `#94949E` | `#656D76` | Folder icons, paths, metadata |
| `MethodGet` | `#22C55E` | `#16A34A` | HTTP `GET` badge background |
| `MethodPost` | `#3B82F6` | `#2563EB` | HTTP `POST` badge background |
| `MethodPut` | `#F59E0B` | `#D97706` | HTTP `PUT` / `PATCH` badge background |
| `MethodDelete` | `#EF4444` | `#DC2626` | HTTP `DELETE` badge background |
| `MethodOther` | `#A855F7` | `#9333EA` | HTTP `HEAD` / `OPTIONS` badge background |
| `StatusDirty` | `#FF9800` | `#D97706` | Unsaved change indicator dot |
| `BadgeAuth` | `#0284C7` | `#0369A1` | Custom Auth Profile badge |
| `BadgeAuthNone` | `#475569` | `#94A3B8` | Explicitly disabled auth badge |

---

## 4. Component Specification Updates

### 4.1 Header: Workspace tabs, environment selector

> **As shipped:** the workspace picker became the title-bar tab strip, and the active-environment
> selector (+ its `Secrets` reveal toggle) now lives on the right of the shell control bar
> (`MainWindow.axaml`), not in the Left Pane. There is no in-app theme selector any more - the theme
> is applied at startup from the persisted preference (`ThemeManagerViewModel.Initialize`, default
> System). The `Status & Log` strip is toggled with <code>Ctrl+`</code> instead of a control-bar button.

```
+-------------------------------------------------------------------+
| Fubar API Studio  [ E-Commerce API x ][ + ]                       |
| [ Import v ]                        [ Env: Staging v ][ Secrets ] |
+-------------------------------------------------------------------+

```

#### Theme options (applied from settings, no in-app switcher)

`ThemeManagerViewModel` still resolves:

1. **`🌙 Dark`**: Sets `Application.Current.RequestedThemeVariant = ThemeVariant.Dark`.
2. **`☀️ Light`**: Sets `Application.Current.RequestedThemeVariant = ThemeVariant.Light`.
3. **`💻 System Default`**: Sets `Application.Current.RequestedThemeVariant = ThemeVariant.Default` (matches OS accent and color scheme automatically).

---

### 4.2 Tree View Adaptability

In Light Mode:

* Tree view hover states change smoothly from `#141416` dark slate to clean `#EAECEF` light grays.
* Method badges maintain `Foreground="White"` with slightly deeper accent shades (`#16A34A` for GET, `#2563EB` for POST) to guarantee WCAG AAA contrast ratio compliance against light backgrounds.
* Selected node highlight (`BgSelected`) uses a soft slate-blue tone (`#DCE3F0`) with a dark text color.

---

## 5. Avalonia XAML Style Definitions Blueprint (Dual-Theme Enabled)

This XAML dictionary demonstrates how `ThemeDictionaries` are structured for Fubar using Avalonia 11.

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:vm="using:Fubar.ViewModels">

  <!-- ============================================================ -->
  <!-- DYNAMIC THEME DICTIONARIES (DARK & LIGHT)                   -->
  <!-- ============================================================ -->
  <ResourceDictionary.ThemeDictionaries>
    
    <!-- DARK THEME VARIANT -->
    <ResourceDictionary x:Key="Dark">
      <SolidColorBrush x:Key="BgSidebar" Color="#141416" />
      <SolidColorBrush x:Key="BgHeader" Color="#1E1E22" />
      <SolidColorBrush x:Key="BgHover" Color="#282830" />
      <SolidColorBrush x:Key="BgSelected" Color="#2D323E" />
      <SolidColorBrush x:Key="BorderSubtle" Color="#2B2B32" />
      <SolidColorBrush x:Key="TextPrimary" Color="#ECECEF" />
      <SolidColorBrush x:Key="TextSecondary" Color="#94949E" />
      <SolidColorBrush x:Key="StatusDirty" Color="#FF9800" />
      
      <SolidColorBrush x:Key="MethodGetBrush" Color="#22C55E" />
      <SolidColorBrush x:Key="MethodPostBrush" Color="#3B82F6" />
      <SolidColorBrush x:Key="MethodPutBrush" Color="#F59E0B" />
      <SolidColorBrush x:Key="MethodDeleteBrush" Color="#EF4444" />
      <SolidColorBrush x:Key="MethodOtherBrush" Color="#A855F7" />
    </ResourceDictionary>

    <!-- LIGHT THEME VARIANT -->
    <ResourceDictionary x:Key="Light">
      <SolidColorBrush x:Key="BgSidebar" Color="#F8F9FA" />
      <SolidColorBrush x:Key="BgHeader" Color="#FFFFFF" />
      <SolidColorBrush x:Key="BgHover" Color="#EAECEF" />
      <SolidColorBrush x:Key="BgSelected" Color="#DCE3F0" />
      <SolidColorBrush x:Key="BorderSubtle" Color="#E1E4E8" />
      <SolidColorBrush x:Key="TextPrimary" Color="#1F2328" />
      <SolidColorBrush x:Key="TextSecondary" Color="#656D76" />
      <SolidColorBrush x:Key="StatusDirty" Color="#D97706" />

      <SolidColorBrush x:Key="MethodGetBrush" Color="#16A34A" />
      <SolidColorBrush x:Key="MethodPostBrush" Color="#2563EB" />
      <SolidColorBrush x:Key="MethodPutBrush" Color="#D97706" />
      <SolidColorBrush x:Key="MethodDeleteBrush" Color="#DC2626" />
      <SolidColorBrush x:Key="MethodOtherBrush" Color="#9333EA" />
    </ResourceDictionary>
  </ResourceDictionary.ThemeDictionaries>

  <!-- ============================================================ -->
  <!-- TREEVIEW & COMPONENT STYLES (DYNAMIC RESOURCE BOUND)        -->
  <!-- ============================================================ -->

  <Style Selector="TreeViewItem">
    <Setter Property="Padding" Value="4,3" />
    <Setter Property="MinHeight" Value="28" />
    <Setter Property="CornerRadius" Value="4" />
    <Setter Property="Foreground" Value="{DynamicResource TextPrimary}" />
  </Style>

  <Style Selector="TreeViewItem:pointerover /template/ Border#PART_LayoutRoot">
    <Setter Property="Background" Value="{DynamicResource BgHover}" />
  </Style>

  <Style Selector="TreeViewItem:selected /template/ Border#PART_LayoutRoot">
    <Setter Property="Background" Value="{DynamicResource BgSelected}" />
  </Style>

  <!-- Request Item Node Template -->
  <TreeDataTemplate x:Key="RequestNodeTemplate" DataType="vm:RequestNodeViewModel">
    <Grid ColumnDefinitions="Auto, *, Auto, Auto" Margin="0,1">
      
      <!-- Method Badge -->
      <Border Grid.Column="0" 
              Width="42" 
              Height="18" 
              CornerRadius="3" 
              Background="{Binding MethodBrush}">
        <TextBlock Text="{Binding Method}" 
                   FontSize="10" 
                   FontWeight="Bold" 
                   Foreground="White" 
                   HorizontalAlignment="Center" 
                   VerticalAlignment="Center"/>
      </Border>

      <!-- Request Name -->
      <TextBlock Grid.Column="1" 
                 Text="{Binding Name}" 
                 Margin="8,0,4,0" 
                 VerticalAlignment="Center"
                 Foreground="{DynamicResource TextPrimary}"
                 TextTrimming="CharacterEllipsis"/>

      <!-- Auth Badge (If Overridden) -->
      <Border Grid.Column="2" 
              IsVisible="{Binding HasAuthOverride}"
              Background="{DynamicResource BadgeAuth}" 
              CornerRadius="3" 
              Padding="4,1" 
              Margin="4,0">
        <TextBlock Text="{Binding AuthProfileName}" FontSize="9" Foreground="White"/>
      </Border>

      <!-- Dirty Indicator Dot -->
      <Ellipse Grid.Column="3" 
               Width="6" 
               Height="6" 
               Fill="{DynamicResource StatusDirty}" 
               IsVisible="{Binding IsDirty}" 
               Margin="4,0,2,0" 
               ToolTip.Tip="Unsaved Changes"/>
    </Grid>
  </TreeDataTemplate>

</ResourceDictionary>

```

---

## 6. Theme Switching C# ViewModel Implementation

```csharp
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Fubar.ViewModels;

public enum AppTheme
{
    System,
    Dark,
    Light
}

public partial class ThemeManagerViewModel : ObservableObject
{
    [ObservableProperty]
    private AppTheme _currentTheme = AppTheme.System;

    [RelayCommand]
    public void SetTheme(AppTheme theme)
    {
        CurrentTheme = theme;
        
        if (Application.Current is null) return;

        Application.Current.RequestedThemeVariant = theme switch
        {
            AppTheme.Dark => ThemeVariant.Dark,
            AppTheme.Light => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };
    }
}

```