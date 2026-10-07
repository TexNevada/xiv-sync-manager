import json
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import release

REPOSITORY = 'TexNevada/xiv-sync-manager'
VERSION = '0.0.1.0'
MANIFEST = {
    'InternalName': 'XivSyncManager', 'Name': 'XIV Sync Manager (Alpha)',
    'AssemblyVersion': VERSION, 'DalamudApiLevel': 15, 'Punchline': 'Manage syncs.',
}
PUBLISHED = {'draft': False, 'assets': [{'name': 'latest.zip'}]}


def feed(channel='dev', version=VERSION):
    manifest = dict(MANIFEST, AssemblyVersion=version)
    return release.make_feed(manifest, channel, REPOSITORY,
                             release.release_tag(channel, version), '2026-10-07T12:00:00Z')


class ReleaseTests(unittest.TestCase):
    def test_bootstrap_channels_use_distinct_tags_and_downloads(self):
        for channel in ('master', 'dev'):
            with self.subTest(channel=channel):
                plan = release.make_plan(channel, VERSION, REPOSITORY, None, [])
                self.assertTrue(plan['publish'])
                self.assertTrue(plan['feed'])
                self.assertIn(plan['tag'], feed(channel)[0]['DownloadLinkInstall'])
        self.assertNotEqual(feed('master')[0]['DownloadLinkInstall'], feed('dev')[0]['DownloadLinkInstall'])

    def test_same_version_does_not_republish_or_change_feed(self):
        for channel in ('master', 'dev'):
            plan = release.make_plan(channel, VERSION, REPOSITORY, PUBLISHED, feed(channel))
            self.assertFalse(plan['publish'])
            self.assertFalse(plan['feed'])

    def test_version_bump_creates_new_release_and_feed(self):
        plan = release.make_plan('dev', '0.0.2.0', REPOSITORY, None, feed())
        self.assertTrue(plan['publish'])
        self.assertTrue(plan['feed'])
        self.assertEqual(plan['tag'], 'dev-v0.0.2.0')

    def test_draft_can_be_retried(self):
        plan = release.make_plan('dev', VERSION, REPOSITORY, {'draft': True, 'assets': []}, feed())
        self.assertTrue(plan['publish'])
        self.assertTrue(plan['feed'])

    def test_failed_feed_commit_can_be_repaired_without_republishing(self):
        plan = release.make_plan('dev', VERSION, REPOSITORY, PUBLISHED, feed('master'))
        self.assertFalse(plan['publish'])
        self.assertTrue(plan['feed'])

    def test_refuses_version_rollback(self):
        with self.assertRaisesRegex(ValueError, 'downgrade'):
            release.make_plan('dev', VERSION, REPOSITORY, None, feed(version='0.0.2.0'))

    def test_does_not_replace_an_incomplete_published_release(self):
        with self.assertRaisesRegex(ValueError, 'no latest.zip'):
            release.make_plan('dev', VERSION, REPOSITORY, {'draft': False, 'assets': []}, feed())

    def test_invalid_channel_and_non_dalamud_version_rejected(self):
        with self.assertRaises(ValueError):
            release.release_tag('main', VERSION)
        for version in ('1.2.3', '1.2.3-dev', '1.2.3.65535'):
            with self.subTest(version=version), self.assertRaises(ValueError):
                release.version_tuple(version)

    def test_feed_is_installable_without_dalamud_testing_mode(self):
        for channel in ('master', 'dev'):
            entry = feed(channel)[0]
            self.assertEqual(entry['InternalName'], MANIFEST['InternalName'])
            self.assertEqual(entry['AssemblyVersion'], MANIFEST['AssemblyVersion'])
            self.assertEqual(entry['DownloadLinkInstall'], entry['DownloadLinkUpdate'])
            self.assertEqual(entry['LastUpdate'], 1791374400)
            self.assertFalse(entry['IsTestingExclusive'])
        self.assertIn('[Dev]', feed()[0]['Name'])
        self.assertNotIn('[Dev]', feed('master')[0]['Name'])

    def test_installer_zip_requires_correct_identity_version_api_and_root_files(self):
        cases = [
            ({}, '', True),
            ({'InternalName': 'OtherPlugin'}, '', False),
            ({'AssemblyVersion': '0.0.2.0'}, '', False),
            ({'DalamudApiLevel': 14}, '', False),
            ({}, 'nested/', False),
        ]
        with tempfile.TemporaryDirectory() as directory:
            for index, (changes, prefix, valid) in enumerate(cases):
                with self.subTest(changes=changes, prefix=prefix):
                    package = Path(directory) / f'{index}.zip'
                    with zipfile.ZipFile(package, 'w') as archive:
                        archive.writestr(prefix + 'XivSyncManager.dll', b'fixture')
                        archive.writestr(prefix + 'XivSyncManager.deps.json', '{}')
                        archive.writestr(prefix + 'XivSyncManager.json', json.dumps(dict(MANIFEST, **changes)))
                    if valid:
                        self.assertEqual(release.read_manifest(package, VERSION), MANIFEST)
                    else:
                        with self.assertRaises(ValueError):
                            release.read_manifest(package, VERSION)


if __name__ == '__main__':
    unittest.main()
