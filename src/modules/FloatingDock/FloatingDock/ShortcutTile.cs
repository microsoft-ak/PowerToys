// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Drawing;
using System.Windows.Forms;

namespace Microsoft.PowerToys.FloatingDock;

internal sealed class ShortcutTile : Button
{
    public ShortcutTile(ShortcutItem item, int index, bool showLabel)
    {
        Item = item;
        Index = index;
        AllowDrop = true;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.FromArgb(245, 247, 250);
        ForeColor = Color.FromArgb(20, 24, 28);
        Margin = new Padding(3);
        Padding = new Padding(4);
        Size = showLabel ? new Size(78, 60) : new Size(44, 40);
        Text = showLabel ? item.Name : string.Empty;
        TextAlign = ContentAlignment.BottomCenter;
        TextImageRelation = showLabel ? TextImageRelation.ImageAboveText : TextImageRelation.Overlay;
        ImageAlign = ContentAlignment.MiddleCenter;
        Image = ShortcutResolver.GetIcon(item, true);
        Tag = index;
        UseVisualStyleBackColor = false;
    }

    public ShortcutItem Item { get; }

    public int Index { get; }
}
