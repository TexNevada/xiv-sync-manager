"""Publish one immutable plugin version per channel, then update its Dalamud feed."""

import argparse
import datetime as dt
import json
import os
from pathlib import Path
import re
import subprocess
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

PROJECT = Path('src/XivSyncManager.csproj')
FEED = Path('xivsyncmanager.json')
PACKAGE = Path('src/bin/x64/Release/XivSyncManager/latest.zip')
PREPARED_FEED = Path('artifacts/xivsyncmanager.json')
PLAN = Path('artifacts/release-plan.json')


def version_tuple(value):
    if not re.fullmatch(r'\d+\.\d+\.\d+\.\d+', value):
        raise ValueError(f'Expected a four-part Dalamud version, got {value!r}')
    parts = tuple(map(int, value.split('.')))
    if any(part > 65534 for part in parts):
        raise ValueError('Assembly version components must be between 0 and 65534')
    return parts


def project_version():
    version = ET.parse(PROJECT).findtext('./PropertyGroup/Version', '').strip()
    version_tuple(version)
    return version


def release_tag(channel, version):
    if channel not in ('master', 'dev'):
        raise ValueError(f'Unsupported release channel: {channel}')
    return f'v{version}' if channel == 'master' else f'dev-v{version}'


def asset_url(repository, tag):
    return f'https://github.com/{repository}/releases/download/{tag}/latest.zip'


def api(path):
    request = urllib.request.Request(
        f'https://api.github.com/repos/{os.environ["GITHUB_REPOSITORY"]}/{path}',
        headers={
            'Authorization': f'Bearer {os.environ["GH_TOKEN"]}',
            'Accept': 'application/vnd.github+json',
            'X-GitHub-Api-Version': '2022-11-28',
        },
    )
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        if error.code == 404:
            return None
        raise


def run(*args):
    subprocess.run(args, check=True)


def output(*args):
    return subprocess.run(args, check=True, capture_output=True, text=True).stdout.strip()


def make_plan(channel, version, repository, release, feed):
    tag = release_tag(channel, version)
    expected_url = asset_url(repository, tag)
    if feed and version_tuple(feed[0]['AssemblyVersion']) > version_tuple(version):
        raise ValueError('Refusing to downgrade the published channel version')
    published = release is not None and not release['draft']
    if published and not any(asset['name'] == 'latest.zip' for asset in release['assets']):
        raise ValueError(f'Published release {tag} has no latest.zip; refusing to replace it')
    matches = (len(feed) == 1 and feed[0].get('AssemblyVersion') == version
               and all(feed[0].get(key) == expected_url
                       for key in ('DownloadLinkInstall', 'DownloadLinkUpdate')))
    return {'channel': channel, 'version': version, 'tag': tag,
            'publish': not published, 'feed': not published or not matches}


def read_manifest(package, version):
    with zipfile.ZipFile(package) as archive:
        # Dalamud installs the ZIP directly: these files must be at its root.
        required = {'XivSyncManager.dll', 'XivSyncManager.deps.json', 'XivSyncManager.json'}
        if not required.issubset(archive.namelist()):
            raise ValueError('Installer ZIP is missing root-level plugin files')
        if archive.testzip() is not None:
            raise ValueError('Installer ZIP failed its integrity check')
        manifest = json.loads(archive.read('XivSyncManager.json').decode('utf-8-sig'))
    if manifest.get('InternalName') != 'XivSyncManager':
        raise ValueError('Installer ZIP has the wrong plugin identity')
    if manifest.get('AssemblyVersion') != version:
        raise ValueError('Installer manifest version does not match the project version')
    if manifest.get('DalamudApiLevel') != 15:
        raise ValueError('Installer ZIP must target Dalamud API 15')
    return manifest


def make_feed(manifest, channel, repository, tag, published_at):
    entry = dict(manifest)
    entry.update({
        'RepoUrl': f'https://github.com/{repository}',
        'DownloadLinkInstall': asset_url(repository, tag),
        'DownloadLinkUpdate': asset_url(repository, tag),
        'IsHide': False,
        # Separate repository URLs select the channel; Dalamud testing mode is unnecessary.
        'IsTestingExclusive': False,
        'LastUpdate': int(dt.datetime.fromisoformat(published_at.replace('Z', '+00:00')).timestamp()),
    })
    if channel == 'dev':
        entry['Name'] = f'{manifest["Name"]} [Dev]'
        entry['Punchline'] = f'Dev build: {manifest["Punchline"]}'
    return [entry]


def plan():
    channel = os.environ['RELEASE_CHANNEL']
    if os.environ['GITHUB_REF'] != f'refs/heads/{channel}':
        raise ValueError('Run the channel workflow on its matching branch')
    version = project_version()
    tag = release_tag(channel, version)
    feed = json.loads(FEED.read_text(encoding='utf-8')) if FEED.exists() else []
    result = make_plan(channel, version, os.environ['GITHUB_REPOSITORY'],
                       api(f'releases/tags/{tag}'), feed)
    PLAN.parent.mkdir(exist_ok=True)
    PLAN.write_text(json.dumps(result), encoding='utf-8')
    with open(os.environ['GITHUB_OUTPUT'], 'a', encoding='utf-8') as output:
        for key in ('publish', 'feed'):
            output.write(f'{key}={str(result[key]).lower()}\n')
    print(f'{channel} {version}: publish={result["publish"]}, update feed={result["feed"]}')


def publish():
    result = json.loads(PLAN.read_text(encoding='utf-8'))
    channel, version, tag = (result[key] for key in ('channel', 'version', 'tag'))
    repository = os.environ['GITHUB_REPOSITORY']
    if result['publish']:
        manifest = read_manifest(PACKAGE, version)
        release = api(f'releases/tags/{tag}')
        if release is not None and not release['draft']:
            raise ValueError('Release was published during this run; refusing to overwrite it')
        # A failed run can leave a draft. Keep the original tag tied to its source commit.
        ref = api(f'git/ref/tags/{tag}')
        if release is not None and ref is None and release.get('target_commitish') != os.environ['GITHUB_SHA']:
            raise ValueError('Existing tagless draft does not target this exact source commit; rerun its original workflow or bump the version')
        if ref is not None:
            target = ref['object']
            while target['type'] == 'tag':
                target = api(f'git/tags/{target["sha"]}')['object']
            if target['sha'] != os.environ['GITHUB_SHA']:
                raise ValueError('Existing release tag points to another commit; rerun its original workflow or bump the version')
        if release is None:
            args = ['gh', 'release', 'create', tag, '--repo', repository, '--draft',
                    '--target', os.environ['GITHUB_SHA'], '--title', f'XIV Sync Manager {version} ({channel})',
                    '--notes', f'{channel} channel, built from {os.environ["GITHUB_SHA"]}.\nInstall using this channel\'s xivsyncmanager.json custom repository URL.']
            if channel == 'dev':
                args.append('--prerelease')
            run(*args)
        run('gh', 'release', 'upload', tag, str(PACKAGE), '--repo', repository, '--clobber')
        # Verify the actual downloadable asset before exposing it in the installer feed.
        run('gh', 'release', 'download', tag, '--repo', repository, '--pattern', 'latest.zip',
            '--dir', 'artifacts/download', '--clobber')
        downloaded = Path('artifacts/download/latest.zip')
        if downloaded.read_bytes() != PACKAGE.read_bytes():
            raise ValueError('Uploaded package does not match the built ZIP')
        read_manifest(downloaded, version)
        run('gh', 'release', 'edit', tag, '--repo', repository, '--draft=false',
            '--latest=true' if channel == 'master' else '--latest=false')
    else:
        # Repair a feed after a previous run uploaded its release but failed to commit the JSON.
        run('gh', 'release', 'download', tag, '--repo', repository, '--pattern', 'latest.zip',
            '--dir', 'artifacts/download', '--clobber')
        manifest = read_manifest(Path('artifacts/download/latest.zip'), version)
    release = api(f'releases/tags/{tag}')
    if release is None or release['draft'] or not release['published_at']:
        raise ValueError('Release is not publicly published; leaving the installer feed unchanged')
    prepared = make_feed(manifest, channel, repository, tag, release['published_at'])
    PREPARED_FEED.write_text(json.dumps(prepared, indent=2) + '\n', encoding='utf-8')


def commit_feed():
    result = json.loads(PLAN.read_text(encoding='utf-8'))
    channel = result['channel']
    prepared = PREPARED_FEED.read_text(encoding='utf-8')
    run('git', 'config', 'user.name', 'github-actions[bot]')
    run('git', 'config', 'user.email', '41898282+github-actions[bot]@users.noreply.github.com')
    if channel == 'master':
        propose_master_feed(result, prepared)
        return
    if channel != 'dev':
        raise ValueError(f'Unsupported feed channel: {channel}')
    # Preserve commits pushed while the build ran; retry a racing fast-forward up to three times.
    for _ in range(3):
        run('git', 'fetch', 'origin', channel)
        run('git', 'checkout', '-B', channel, f'origin/{channel}')
        current = json.loads(FEED.read_text(encoding='utf-8')) if FEED.exists() else []
        if current and version_tuple(current[0]['AssemblyVersion']) > version_tuple(result['version']):
            raise ValueError('A newer feed is already published; refusing to downgrade it')
        if FEED.exists() and FEED.read_text(encoding='utf-8') == prepared:
            print('Installer feed already matches the release')
            return
        FEED.write_text(prepared, encoding='utf-8')
        run('git', 'add', str(FEED))
        run('git', 'commit', '-m', f'Update {channel} installer feed for {result["version"]}')
        if subprocess.run(['git', 'push', 'origin', f'HEAD:refs/heads/{channel}']).returncode == 0:
            return
    raise RuntimeError('Could not push the feed after three attempts; rerun the workflow to repair it')


def propose_master_feed(result, prepared):
    # Master is human-reviewed. Only push an ordinary working branch; never merge its PR.
    branch = f'release/master-feed-{result["version"]}'
    repository = os.environ['GITHUB_REPOSITORY']
    run('git', 'fetch', 'origin', 'master')
    run('git', 'checkout', '-B', branch, 'origin/master')
    current = json.loads(FEED.read_text(encoding='utf-8')) if FEED.exists() else []
    if current and version_tuple(current[0]['AssemblyVersion']) > version_tuple(result['version']):
        raise ValueError('A newer master feed is already published; refusing to downgrade it')
    if FEED.exists() and FEED.read_text(encoding='utf-8') == prepared:
        print('Master installer feed already matches the release')
        return
    if output('git', 'ls-remote', '--heads', 'origin', f'refs/heads/{branch}'):
        run('git', 'fetch', 'origin', branch)
        # Preserve an existing PR branch and human commits rather than force-pushing it.
        run('git', 'checkout', '-B', branch, f'origin/{branch}')
    current = json.loads(FEED.read_text(encoding='utf-8')) if FEED.exists() else []
    if current and version_tuple(current[0]['AssemblyVersion']) > version_tuple(result['version']):
        raise ValueError('The existing feed PR has a newer version; refusing to downgrade it')
    if not FEED.exists() or FEED.read_text(encoding='utf-8') != prepared:
        FEED.write_text(prepared, encoding='utf-8')
        run('git', 'add', str(FEED))
        run('git', 'commit', '-m', f'Update master installer feed for {result["version"]}')
        run('git', 'push', 'origin', f'HEAD:refs/heads/{branch}')
    # Include the head owner so a similarly named branch in a fork cannot be reused.
    url = output('gh', 'api', '--method', 'GET', f'repos/{repository}/pulls',
                 '-f', 'state=open', '-f', 'base=master', '-f', f'head={repository.split("/")[0]}:{branch}',
                 '--jq', '.[0].html_url // empty')
    if not url:
        body = Path('artifacts/master-feed-pr.md')
        body.parent.mkdir(exist_ok=True)
        body.write_text(
            f'Update the Dalamud installer feed to master version {result["version"]}.\n\n'
            f'Package: {asset_url(repository, result["tag"])}\n\n'
            'The release workflow verified the package and manifest. '
            'This PR updates the installer feed only and remains open for human review and merging.\n',
            encoding='utf-8')
        url = output('gh', 'pr', 'create', '--repo', repository, '--base', 'master', '--head', branch,
                     '--title', f'Update master installer feed for {result["version"]}', '--body-file', str(body))
    print(f'Master installer feed awaits human review: {url}')
    if summary := os.environ.get('GITHUB_STEP_SUMMARY'):
        with open(summary, 'a', encoding='utf-8') as stream:
            stream.write(f'Master installer feed awaits human review: {url}\n')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('plan', 'publish', 'commit-feed'))
    arguments = parser.parse_args()
    {'plan': plan, 'publish': publish, 'commit-feed': commit_feed}[arguments.command]()
