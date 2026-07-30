/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.App.Effects;

internal static class ProgressiveBlurResourceKeys
{
    internal const string IsEnabled = "Is.ProgressiveBlur.Enabled";
    internal const string MaximumRadius = "ListPage.ProgressiveBlur.MaxRadius";
    internal const string RenderScale = "ListPage.ProgressiveBlur.RenderScale";
    internal const string ActiveMinimumOpacity = "ListPage.ProgressiveBlur.ActiveMinimumOpacity";
    internal const string ActiveIntermediateOpacity = "ListPage.ProgressiveBlur.ActiveIntermediateOpacity";
}

internal static class ProgressiveBlurDefaults
{
    internal const double MaximumRadius = 24d;
    internal const double RenderScale = 0.2d;
    internal const double ActiveMinimumOpacity = 0d;
    internal const double ActiveIntermediateOpacity = 0.4d;
    internal const double SamplingGuardLength = 24d;
    internal const double TextureOverscanLength = 4d;
    internal const double MinimumRenderScale = 0.1d;
}
