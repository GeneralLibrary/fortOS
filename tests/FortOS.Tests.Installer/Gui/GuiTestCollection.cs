namespace FortOS.Tests.Installer.Gui;

/// <summary>
/// 串行化所有使用 <see cref="LocalizationService.Current"/> 静态单例的 GUI 测试类。
/// xUnit 默认并行执行不同测试类;LocalizationServiceTests 执行中会 SetLanguage("zh"),
/// 若与断言英文文案的测试(如 PageViewModelTests)并行,后者的断言会读到中文而失败
/// (CI 上已确定性出现)。DisableParallelization = true 使本 collection 内测试串行执行,
/// 配合 LocalizationServiceTests 自身的 Reset() 模式,消除跨类语言状态污染。
/// </summary>
[CollectionDefinition("Gui.Localization", DisableParallelization = true)]
public sealed class GuiLocalizationCollection;
