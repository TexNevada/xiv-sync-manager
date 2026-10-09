"""Exercise publication and feed mutations with Git/GitHub operations substituted."""
import json
import os
from pathlib import Path
import shutil
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import release

VERSION = '0.0.1.7'
REPOSITORY = 'TexNevada/xiv-sync-manager'
SOURCE = 'a' * 40
MANIFEST = {'InternalName': 'XivSyncManager', 'Name': 'Manager', 'AssemblyVersion': VERSION,
            'DalamudApiLevel': 15, 'Punchline': 'Manage syncs.'}


class ReleaseActionTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        previous = Path.cwd()
        os.chdir(self.root)
        self.addCleanup(os.chdir, previous)
        self.plan = {'channel': 'dev', 'version': VERSION, 'tag': f'dev-v{VERSION}', 'publish': True}
        self.root.joinpath('artifacts').mkdir()
        self.commands = []
        for name, path in [('PLAN', 'artifacts/plan.json'), ('PREPARED_FEED', 'artifacts/prepared.json'),
                           ('FEED', 'feed.json'), ('PACKAGE', 'package.zip')]:
            self.enterContext(patch.object(release, name, self.root / path))
        self.enterContext(patch.dict(os.environ, {'GITHUB_REPOSITORY': REPOSITORY, 'GITHUB_SHA': SOURCE}))
        with zipfile.ZipFile(release.PACKAGE, 'w') as archive:
            archive.writestr('XivSyncManager.dll', b'fixture')
            archive.writestr('XivSyncManager.deps.json', '{}')
            archive.writestr('XivSyncManager.json', json.dumps(MANIFEST))
        self.published = False

    def plan_file(self):
        release.PLAN.write_text(json.dumps(self.plan), encoding='utf-8')

    def test_plan_rejects_other_branches_and_tags_before_accessing_github(self):
        for channel in ('dev', 'master'):
            for ref in ('refs/heads/release/0.1.0.0', 'refs/heads/feature/settings',
                        'refs/tags/v0.1.0.0', f'refs/heads/{"master" if channel == "dev" else "dev"}'):
                with self.subTest(channel=channel, ref=ref), patch.dict(os.environ, {
                    'RELEASE_CHANNEL': channel, 'GITHUB_REF': ref,
                }), patch.object(release, 'api') as api:
                    with self.assertRaisesRegex(ValueError, 'matching branch'):
                        release.plan()
                    api.assert_not_called()
                    self.assertFalse(release.PLAN.exists())

    def test_plan_accepts_only_matching_channel_branches(self):
        for channel in ('dev', 'master'):
            with self.subTest(channel=channel), patch.dict(os.environ, {
                'RELEASE_CHANNEL': channel, 'GITHUB_REF': f'refs/heads/{channel}',
                'GITHUB_OUTPUT': str(self.root / 'output'),
            }), patch.object(release, 'project_version', return_value=VERSION), \
                    patch.object(release, 'api', return_value=None):
                release.plan()
                result = json.loads(release.PLAN.read_text())
                self.assertEqual(result['channel'], channel)
                self.assertTrue(result['publish'])

    def simulate_run(self, *args):
        self.commands.append(args)
        if args[:3] == ('gh', 'release', 'download'):
            destination = Path('artifacts/download')
            destination.mkdir(exist_ok=True)
            shutil.copyfile(release.PACKAGE, destination / 'latest.zip')
        if args[:3] == ('gh', 'release', 'edit'):
            self.published = True

    def publish_api(self, draft, ref=None):
        def api(path):
            if path.startswith('releases/tags/'):
                return dict(draft, draft=False, published_at='2026-10-09T12:00:00Z') if self.published else draft
            if path.startswith('git/ref/'):
                return ref
            raise AssertionError(path)
        return api

    def test_tagless_draft_from_another_commit_is_rejected_before_upload(self):
        self.plan_file()
        draft = {'draft': True, 'target_commitish': 'b' * 40}
        with patch.object(release, 'api', self.publish_api(draft)), patch.object(release, 'run', self.simulate_run):
            with self.assertRaisesRegex(ValueError, 'exact source commit'):
                release.publish()
        self.assertEqual(self.commands, [])
        self.assertFalse(release.PREPARED_FEED.exists())

    def test_tagless_draft_with_unpinned_branch_target_is_rejected(self):
        self.plan_file()
        with patch.object(release, 'api', self.publish_api({'draft': True, 'target_commitish': 'dev'})), patch.object(release, 'run', self.simulate_run):
            with self.assertRaises(ValueError):
                release.publish()
        self.assertEqual(self.commands, [])

    def test_matching_tagless_draft_can_resume_without_recreating_release(self):
        self.plan_file()
        with patch.object(release, 'api', self.publish_api({'draft': True, 'target_commitish': SOURCE})), patch.object(release, 'run', self.simulate_run):
            release.publish()
        self.assertTrue(self.published)
        self.assertNotIn(('gh', 'release', 'create'), [command[:3] for command in self.commands])
        self.assertEqual(json.loads(release.PREPARED_FEED.read_text())[0]['AssemblyVersion'], VERSION)

    def test_existing_wrong_tag_is_rejected_before_upload(self):
        self.plan_file()
        ref = {'object': {'type': 'commit', 'sha': 'b' * 40}}
        with patch.object(release, 'api', self.publish_api({'draft': True, 'target_commitish': SOURCE}, ref)), patch.object(release, 'run', self.simulate_run):
            with self.assertRaisesRegex(ValueError, 'another commit'):
                release.publish()
        self.assertEqual(self.commands, [])

    def test_valid_existing_tag_is_authoritative_even_if_draft_target_is_a_branch(self):
        self.plan_file()
        ref = {'object': {'type': 'commit', 'sha': SOURCE}}
        with patch.object(release, 'api', self.publish_api({'draft': True, 'target_commitish': 'dev'}, ref)), patch.object(release, 'run', self.simulate_run):
            release.publish()
        self.assertTrue(self.published)

    def prepare_master_feed(self):
        self.plan.update(channel='master', tag=f'v{VERSION}')
        self.plan_file()
        prepared = release.make_feed(MANIFEST, 'master', REPOSITORY, self.plan['tag'], '2026-10-09T12:00:00Z')
        release.PREPARED_FEED.write_text(json.dumps(prepared, indent=2) + '\n', encoding='utf-8')
        release.FEED.write_text(json.dumps([dict(prepared[0], AssemblyVersion='0.0.1.6')]), encoding='utf-8')
        return prepared

    def test_master_feed_pushes_only_a_working_branch_and_creates_open_pr(self):
        self.prepare_master_feed()
        def output(*args):
            self.commands.append(args)
            if args[:2] == ('git', 'ls-remote') or args[:2] == ('gh', 'api'):
                return ''
            if args[:3] == ('gh', 'pr', 'create'):
                return 'https://github.com/example/repo/pull/1'
            raise AssertionError(args)
        with patch.object(release, 'run', self.simulate_run), patch.object(release, 'output', output):
            release.commit_feed()
        pushes = [command for command in self.commands if command[:2] == ('git', 'push')]
        self.assertEqual(pushes, [('git', 'push', 'origin', f'HEAD:refs/heads/release/master-feed-{VERSION}')])
        self.assertIn(('gh', 'pr', 'create'), [command[:3] for command in self.commands])
        self.assertFalse(any(command[:2] == ('git', 'merge') or command[:3] == ('gh', 'pr', 'merge') for command in self.commands))
        self.assertIn('human review', Path('artifacts/master-feed-pr.md').read_text())

    def test_existing_pr_branch_is_preserved_without_force_push_or_duplicate_pr(self):
        self.prepare_master_feed()
        branch = f'release/master-feed-{VERSION}'
        def run(*args):
            self.simulate_run(*args)
            if args == ('git', 'checkout', '-B', branch, f'origin/{branch}'):
                release.FEED.write_text(release.PREPARED_FEED.read_text(), encoding='utf-8')
        def output(*args):
            self.commands.append(args)
            if args[:2] == ('git', 'ls-remote'):
                return f'{SOURCE}\trefs/heads/{branch}'
            if args[:2] == ('gh', 'api'):
                return 'https://github.com/example/repo/pull/1'
            raise AssertionError(args)
        with patch.object(release, 'run', run), patch.object(release, 'output', output):
            release.commit_feed()
        self.assertIn(('git', 'checkout', '-B', branch, f'origin/{branch}'), self.commands)
        self.assertFalse(any(command[:2] in [('git', 'push'), ('git', 'commit')] for command in self.commands))
        self.assertNotIn(('gh', 'pr', 'create'), [command[:3] for command in self.commands])

    def test_master_feed_already_merged_needs_no_push_or_pr(self):
        self.prepare_master_feed()
        release.FEED.write_text(release.PREPARED_FEED.read_text(), encoding='utf-8')
        with patch.object(release, 'run', self.simulate_run), patch.object(release, 'output') as output:
            release.commit_feed()
        output.assert_not_called()
        self.assertFalse(any(command[:2] == ('git', 'push') for command in self.commands))

    def test_master_feed_refuses_downgrade(self):
        prepared = self.prepare_master_feed()
        release.FEED.write_text(json.dumps([dict(prepared[0], AssemblyVersion='0.0.1.8')]), encoding='utf-8')
        with patch.object(release, 'run', self.simulate_run), patch.object(release, 'output') as output:
            with self.assertRaisesRegex(ValueError, 'downgrade'):
                release.commit_feed()
        output.assert_not_called()
        self.assertFalse(any(command[:2] == ('git', 'push') for command in self.commands))

    def test_unknown_channel_never_pushes(self):
        self.prepare_master_feed()
        self.plan['channel'] = 'other'
        self.plan_file()
        with patch.object(release, 'run', self.simulate_run):
            with self.assertRaisesRegex(ValueError, 'Unsupported'):
                release.commit_feed()
        self.assertFalse(any(command[:2] == ('git', 'push') for command in self.commands))

    def test_dev_feed_retries_a_racing_push_without_creating_a_pr(self):
        self.prepare_master_feed()
        self.plan.update(channel='dev', tag=f'dev-v{VERSION}')
        self.plan_file()
        older = release.FEED.read_text()
        attempts = 0
        def run(*args):
            self.simulate_run(*args)
            if args == ('git', 'checkout', '-B', 'dev', 'origin/dev'):
                release.FEED.write_text(older, encoding='utf-8')
        def push(args):
            nonlocal attempts
            self.commands.append(tuple(args))
            self.assertEqual(args, ['git', 'push', 'origin', 'HEAD:refs/heads/dev'])
            attempts += 1
            return SimpleNamespace(returncode=1 if attempts == 1 else 0)
        with patch.object(release, 'run', run), patch.object(release.subprocess, 'run', push), patch.object(release, 'output') as output:
            release.commit_feed()
        output.assert_not_called()
        self.assertEqual(attempts, 2)
        self.assertEqual(self.commands.count(('git', 'fetch', 'origin', 'dev')), 2)
        self.assertEqual(release.FEED.read_text(), release.PREPARED_FEED.read_text())
        self.assertFalse(any(command[0] == 'gh' for command in self.commands))

    def test_dev_feed_never_overwrites_a_newer_published_version(self):
        prepared = self.prepare_master_feed()
        self.plan['channel'] = 'dev'
        self.plan_file()
        release.FEED.write_text(json.dumps([dict(prepared[0], AssemblyVersion='0.0.1.8')]), encoding='utf-8')
        with patch.object(release, 'run', self.simulate_run), patch.object(release.subprocess, 'run') as push:
            with self.assertRaisesRegex(ValueError, 'downgrade'):
                release.commit_feed()
        push.assert_not_called()


if __name__ == '__main__':
    unittest.main()
