// Two small jobs: point the download button at the visitor's platform, and keep the page tidy
// while the launch video isn't there. Nothing here talks to the network.
(function () {
  'use strict';

  var RELEASES = 'https://github.com/hazeliscoding/prompuff/releases/latest/download/';

  var PLATFORMS = {
    windows: {
      group: 'windows',
      asset: 'Prompuff-Setup.exe',
      label: 'Download for Windows',
      note: 'Prompuff-Setup.exe, signed, for Windows 10 and 11.'
    },
    mac: {
      group: 'mac',
      asset: 'Prompuff-macOS-arm64.pkg',
      label: 'Download for Mac',
      note: 'For Apple Silicon. Intel Macs have their own build below.'
    },
    'linux-x64': {
      group: 'linux',
      asset: 'Prompuff-linux-x64.AppImage',
      label: 'Download for Linux',
      note: 'The AppImage for x64.'
    },
    'linux-arm64': {
      group: 'linux',
      asset: 'Prompuff-linux-arm64.AppImage',
      label: 'Download for Linux',
      note: 'The AppImage for ARM64.'
    }
  };

  function detectPlatform() {
    var ua = navigator.userAgent || '';
    var uaPlatform = navigator.userAgentData && navigator.userAgentData.platform;
    var platform = uaPlatform || navigator.platform || '';
    var both = platform + ' ' + ua;

    // Phones, tablets and Chromebooks get the full list instead of a guess.
    if (/android|iphone|ipad|ipod|cros|chrome os/i.test(both)) return null;
    // iPadOS asks for the desktop site and says it's a Mac.
    if (/mac/i.test(platform) && navigator.maxTouchPoints > 1) return null;

    if (/win/i.test(platform) || /windows/i.test(ua)) return 'windows';
    if (/mac/i.test(platform) || /mac os x/i.test(ua)) return 'mac';
    if (/linux|x11/i.test(both)) return /aarch64|arm64|armv8/i.test(both) ? 'linux-arm64' : 'linux-x64';
    return null;
  }

  function pickDownload() {
    var key = detectPlatform();
    var choice = key && PLATFORMS[key];
    if (!choice) return;

    var button = document.getElementById('get');
    var label = document.getElementById('get-label');
    var note = document.getElementById('get-note');
    var other = document.getElementById('get-other');
    if (button && label) {
      button.href = RELEASES + choice.asset;
      label.textContent = choice.label;
    }
    if (note) note.textContent = choice.note;
    if (other) other.hidden = false;

    var group = document.querySelector('.platform[data-platform="' + choice.group + '"]');
    if (!group) return;
    group.classList.add('is-current');
    var badge = group.querySelector('.badge');
    if (badge) badge.hidden = false;
    var file = group.querySelector('.file[data-asset="' + choice.asset + '"]');
    if (file) file.classList.add('is-primary');
  }

  // Until prompuff-launch.mp4 is in place, show a friendly stand-in instead of a broken player.
  function watchVideo() {
    var figure = document.getElementById('launch-video');
    if (!figure) return;
    var video = figure.querySelector('video');
    var missing = figure.querySelector('.video-missing');
    if (!video || !missing) return;

    function showMissing() {
      figure.classList.add('is-missing');
      missing.hidden = false;
    }

    var sources = video.querySelectorAll('source');
    var last = sources[sources.length - 1];
    if (last) last.addEventListener('error', showMissing);
    video.addEventListener('error', showMissing);

    // The request may already have failed before this script ran.
    if (video.networkState === HTMLMediaElement.NETWORK_NO_SOURCE) showMissing();
  }

  pickDownload();
  watchVideo();
})();
