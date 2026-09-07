using System.Threading.Tasks;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// A main-canvas editor the shell can save without knowing which kind it is.
///
/// <para>Exists so <c>Ctrl+S</c> works on all three surfaces - a request, an environment, an auth
/// profile - rather than only on requests. A shortcut that works on two screens out of three is worse
/// than none, because the two that work are what teach you to trust it.</para>
///
/// <para>Each of these already had a <c>SaveCommand</c>; what was missing was any way for the shell to
/// reach it, since <see cref="MainViewModel.ActiveEditor"/> is deliberately typed as <c>object</c> -
/// the canvas holds one of several unrelated view models and nothing else about it is shared.</para>
/// </summary>
public interface ISaveableEditor
{
    /// <summary>Writes the editor's changes. Reporting failure is the editor's own business.</summary>
    Task SaveAsync();
}
