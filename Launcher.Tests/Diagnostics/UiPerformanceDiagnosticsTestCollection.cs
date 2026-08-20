/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Tests.Diagnostics;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UiPerformanceDiagnosticsTestCollection
{
    public const string Name = "UI performance diagnostics";
}
