﻿using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace amethyst_installer_gui
{
    /// <summary>
    /// Utility class to fetch accent colors from Windows without having access to modern features
    /// </summary>
    public static class WindowsColorHelpers
    {
        public static Brush BorderAccent { get { return SystemParameters.WindowGlassBrush; } }
        public static Brush Accent { get { return new SolidColorBrush(GetAccentColor()); } }
        public static Brush AccentText { get { return new SolidColorBrush(GetContrastingColor(GetAccentColor())); } }

        // Extended from
        // https://stackoverflow.com/a/50848113
        public static Color GetAccentColor()
        {
            const string DWM_KEY = @"Software\Microsoft\Windows\DWM";
            using (RegistryKey dwmKey = Registry.CurrentUser.OpenSubKey(DWM_KEY, RegistryKeyPermissionCheck.ReadSubTree))
            {
                if (dwmKey is null)
                {
                    // const string KEY_EX_MSG = "The \"HKCU\\" + DWM_KEY + "\" registry key does not exist.";
                    // throw new InvalidOperationException(KEY_EX_MSG);

                    // Fallback to default accent color: teal blue
                    return Color.FromRgb(24, 131, 215);
                }

                object accentColorObj = dwmKey.GetValue("AccentColor");
                if (accentColorObj is int accentColorDword)
                {
                    return ParseDWordColor(accentColorDword);
                }
                else
                {
                    // const string VALUE_EX_MSG = "The \"HKCU\\" + DWM_KEY + "\\AccentColor\" registry key value could not be parsed as an ABGR color.";
                    // throw new InvalidOperationException(VALUE_EX_MSG);
                }
            }

            // Fallback to default accent color: teal blue
            return Color.FromRgb(24, 131, 215);
        }

        /// <summary>
        /// Returns a color which maintains contrast with the given color. Useful for getting text colors
        /// </summary>
        public static Color GetContrastingColor(Color color)
        {
            return (Luminosity(color) >= 165) ? Color.FromRgb(0,0,0) : Color.FromRgb(255, 255, 255);
        }

        /// <summary>
        /// Returns the luminosity of a color according to BT.709-1
        /// </summary>
        public static float Luminosity(Color color)
        {
            // Convert to 0 to 1 like a shader
            float colorR = color.R / 255f;
            float colorG = color.G / 255f;
            float colorB = color.B / 255f;

            // dot product with luma co-efficients for BT.709-1
            colorR *= 0.2125f;
            colorG *= 0.7154f;
            colorB *= 0.0721f;
            return colorR + colorG + colorB;
        }

        private static Color ParseDWordColor(int color)
        {
            byte
                a = (byte)((color >> 24) & 0xFF),
                b = (byte)((color >> 16) & 0xFF),
                g = (byte)((color >> 8) & 0xFF),
                r = (byte)((color >> 0) & 0xFF);

            return Color.FromRgb(r, g, b);
        }

    }
}
