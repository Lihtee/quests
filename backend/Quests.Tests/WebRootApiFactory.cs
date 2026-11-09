namespace Quests.Tests;

/// <summary><see cref="ApiFactory"/> that serves a temp web root with <c>index.html</c> and <c>assets/app.js</c>.</summary>
public sealed class WebRootApiFactory() : ApiFactory(withWebRoot: true, settings: null);
