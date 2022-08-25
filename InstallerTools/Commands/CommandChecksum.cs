﻿using CommandLine;
using System;
using System.IO;
using AmeUtil = amethyst_installer_gui.Util;

namespace InstallerTools.Commands {

    [Verb("checksum", aliases: new string[] { "k" }, HelpText = "Calculates the MD5 checksum of a given file.", Hidden = false)]
    public class CommandChecksum : ICommand {

        [Option('p', "filepath", HelpText = "The path to the specified file")]
        public string FilePath { get; set; }

        public void Execute() {

            FilePath = Path.GetFullPath(FilePath);

            var checksum = AmeUtil.GetChecksum(FilePath);
            Console.WriteLine(checksum);

            return;
        }
    }
}
