"""Guard the plugin's command registration and unload wiring."""
from pathlib import Path
import re
import unittest


class CommandTests(unittest.TestCase):
    def test_short_alias_uses_existing_handler_and_is_removed_on_unload(self):
        source = (Path(__file__).resolve().parents[2] / 'src' / 'Plugin.cs').read_text()
        constants = dict(re.findall(r'private const string (\w+) = "([^"]+)";', source))
        for command in ('/syncmanager', '/xsm'):
            with self.subTest(command=command):
                names = [name for name, value in constants.items() if value == command]
                self.assertEqual(len(names), 1, f'{command} must have a command constant')
                name = names[0]
                self.assertRegex(source, rf'CommandManager\.AddHandler\({name}, new CommandInfo\(OnCommand\)')
                self.assertIn(f'CommandManager.RemoveHandler({name});', source)
        self.assertIn('args.Trim().Equals("config", StringComparison.OrdinalIgnoreCase)', source)


if __name__ == '__main__':
    unittest.main()
