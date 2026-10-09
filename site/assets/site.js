// Prompuff's landing page. It points the download buttons at the visitor's platform, keeps the page tidy
// while the launch video isn't there, and runs the scroll and theme effects. Nothing here talks to the
// network, and every effect steps aside when the system asks for reduced motion.
(function () {
  'use strict';

  var root = document.documentElement;
  root.classList.add('js');

  var reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');

  function motionAllowed() {
    return !reducedMotion.matches;
  }

  function syncMotion() {
    root.classList.toggle('motion', motionAllowed());
  }

  function clamp(value, low, high) {
    return Math.min(high, Math.max(low, value));
  }

  function each(list, fn) {
    Array.prototype.forEach.call(list, fn);
  }

  function onceVisible(elements, threshold, fn) {
    if (!('IntersectionObserver' in window)) {
      each(elements, fn);
      return;
    }
    var observer = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (!entry.isIntersecting) return;
        observer.unobserve(entry.target);
        fn(entry.target);
      });
    }, { rootMargin: '0px 0px -8% 0px', threshold: threshold });
    each(elements, function (el) { observer.observe(el); });
  }

  // Scroll scenes share one frame callback, so the page reads layout once per frame at most.

  var scenes = [];
  var ticking = false;

  function runScenes() {
    ticking = false;
    var vh = window.innerHeight;
    for (var i = 0; i < scenes.length; i++) scenes[i](vh);
  }

  function requestUpdate() {
    if (ticking) return;
    ticking = true;
    window.requestAnimationFrame(runScenes);
  }

  // Download buttons

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

    [['get', 'get-label'], ['get-again', 'get-again-label']].forEach(function (ids) {
      var button = document.getElementById(ids[0]);
      var label = document.getElementById(ids[1]);
      if (!button || !label) return;
      button.href = RELEASES + choice.asset;
      label.textContent = choice.label;
    });

    var note = document.getElementById('get-note');
    var other = document.getElementById('get-other');
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

  // The header turns from clear glass to glass with an edge once the page moves.

  function setupHeader() {
    var header = document.getElementById('site-header');
    if (!header) return;
    scenes.push(function () {
      header.classList.toggle('is-scrolled', window.scrollY > 4);
    });
  }

  // The launch film. Until prompuff-launch.mp4 is in place, a friendly stand-in shows instead of a broken
  // player. Otherwise one quiet play button stands in for the browser's controls until the film starts, and
  // the frame grows to full size as it scrolls in.

  function setupVideo() {
    var figure = document.getElementById('launch-video');
    if (!figure) return;
    var video = figure.querySelector('video');
    var missing = figure.querySelector('.video-missing');
    var play = figure.querySelector('.video-play');
    var stage = figure.querySelector('.video-stage');
    var section = figure.closest('.launch') || figure;
    var watch = document.getElementById('watch');
    if (!video || !missing) return;

    function showMissing() {
      figure.classList.add('is-missing');
      missing.hidden = false;
      if (play) play.hidden = true;
    }

    function start() {
      if (figure.classList.contains('is-missing')) return;
      if (play) play.hidden = true;
      video.controls = true;
      var attempt = video.play();
      if (attempt && attempt.catch) attempt.catch(function () {});
    }

    var sources = video.querySelectorAll('source');
    var last = sources[sources.length - 1];
    if (last) last.addEventListener('error', showMissing);
    video.addEventListener('error', showMissing);

    // The request may already have failed before this script ran.
    if (video.networkState === HTMLMediaElement.NETWORK_NO_SOURCE) {
      showMissing();
    } else if (play) {
      video.controls = false;
      play.hidden = false;
      play.addEventListener('click', function () {
        start();
        video.focus({ preventScroll: true });
      });
    }

    video.addEventListener('play', function () {
      if (play) play.hidden = true;
      video.controls = true;
    });

    if (watch) {
      watch.addEventListener('click', function (event) {
        event.preventDefault();
        start();
        figure.scrollIntoView({ behavior: motionAllowed() ? 'smooth' : 'auto', block: 'center' });
        if (!figure.classList.contains('is-missing')) video.focus({ preventScroll: true });
      });
    }

    if (!stage) return;
    scenes.push(function (vh) {
      if (!motionAllowed()) {
        stage.style.removeProperty('--video-scale');
        figure.style.removeProperty('--video-glow');
        return;
      }
      var top = section.getBoundingClientRect().top;
      var progress = clamp((vh - top) / (vh * 0.75), 0, 1);
      var eased = 1 - Math.pow(1 - progress, 3);
      stage.style.setProperty('--video-scale', (0.88 + 0.12 * eased).toFixed(4));
      figure.style.setProperty('--video-glow', eased.toFixed(3));
    });
  }

  // Headings, tiles and cards rise into place the first time they scroll in. Siblings in a group follow
  // each other by a beat.

  function setupReveals() {
    each(document.querySelectorAll('[data-stagger]'), function (group) {
      each(group.querySelectorAll('[data-reveal]'), function (el, index) {
        el.style.setProperty('--reveal-delay', index * 110 + 'ms');
      });
    });
    onceVisible(document.querySelectorAll('[data-reveal]'), 0.12, function (el) {
      el.classList.add('is-in');
    });
  }

  // The bento tiles play their small demo once: the search types its query, the keys press, the zip unpacks.

  function setupDemos() {
    var search = document.querySelector('.search-demo');
    var query = search && search.querySelector('.search-query');
    var text = query ? query.getAttribute('data-text') || query.textContent : '';
    if (query && motionAllowed()) query.textContent = '';

    function typeQuery() {
      var typed = 0;
      (function next() {
        if (typed < text.length) {
          typed++;
          query.textContent = text.slice(0, typed);
          window.setTimeout(next, 130);
        } else {
          window.setTimeout(function () { search.classList.add('is-found'); }, 200);
        }
      })();
    }

    onceVisible(document.querySelectorAll('[data-demo]'), 0.45, function (demo) {
      demo.classList.add('is-played');
      if (demo !== search || !query) return;
      if (!motionAllowed()) {
        query.textContent = text;
        search.classList.add('is-found');
        return;
      }
      window.setTimeout(typeQuery, 350);
    });
  }

  // The feature tour. On wide screens each step's words scroll past while its screenshot stays in view; the
  // step nearest the middle of the window is the one showing.

  function setupTour() {
    var tour = document.getElementById('tour');
    if (!tour) return;
    var steps = tour.querySelectorAll('.step');
    var texts = tour.querySelectorAll('.step-text');
    var wide = window.matchMedia('(min-width: 901px)');
    var active = 0;
    if (!steps.length) return;

    scenes.push(function (vh) {
      if (!wide.matches) return;
      var middle = vh / 2;
      var best = active;
      var bestDistance = Infinity;
      for (var i = 0; i < texts.length; i++) {
        var rect = texts[i].getBoundingClientRect();
        var distance = Math.abs(rect.top + rect.height / 2 - middle);
        if (distance < bestDistance) {
          bestDistance = distance;
          best = i;
        }
      }
      if (best === active) return;
      steps[active].classList.remove('is-active');
      steps[best].classList.add('is-active');
      active = best;
    });
  }

  // The themes showroom. Each swatch re-dresses the prompt; the registered colors in site.css make the
  // change glide. While it's on screen it tours the themes by itself, until a swatch is picked or the tour
  // is paused.

  function setupThemes() {
    var stage = document.getElementById('theme-stage');
    var picker = document.getElementById('theme-picker');
    var nameEl = document.getElementById('theme-name');
    var names = document.getElementById('theme-names');
    var pause = document.getElementById('theme-pause');
    var showroom = document.getElementById('showroom');
    if (!stage || !picker || !nameEl) return;
    var dots = picker.querySelectorAll('.dot');
    if (!dots.length) return;

    var current = 0;
    var timer = null;
    var swapTimer = null;
    var stopped = false;
    var visible = false;

    picker.hidden = false;
    if (names) names.hidden = true;

    function show(index) {
      var previous = dots[current];
      var next = dots[index];
      stage.classList.remove(previous.getAttribute('data-theme'));
      stage.classList.add(next.getAttribute('data-theme'));
      previous.setAttribute('aria-pressed', 'false');
      next.setAttribute('aria-pressed', 'true');
      current = index;

      var label = next.getAttribute('aria-label');
      window.clearTimeout(swapTimer);
      if (!motionAllowed()) {
        nameEl.textContent = label;
        return;
      }
      nameEl.classList.add('is-swapping');
      swapTimer = window.setTimeout(function () {
        nameEl.textContent = label;
        nameEl.classList.remove('is-swapping');
      }, 180);
    }

    function syncTour() {
      var playing = !stopped && visible && motionAllowed() && !document.hidden;
      if (playing && !timer) {
        timer = window.setInterval(function () { show((current + 1) % dots.length); }, 2600);
      } else if (!playing && timer) {
        window.clearInterval(timer);
        timer = null;
      }
      if (pause) {
        pause.classList.toggle('is-paused', stopped);
        pause.setAttribute('aria-label', stopped ? 'Play the theme tour' : 'Pause the theme tour');
      }
    }

    each(dots, function (dot, index) {
      dot.addEventListener('click', function () {
        stopped = true;
        show(index);
        syncTour();
      });
    });

    if (pause && motionAllowed()) {
      pause.hidden = false;
      pause.addEventListener('click', function () {
        stopped = !stopped;
        syncTour();
      });
    }

    if (showroom && 'IntersectionObserver' in window) {
      new IntersectionObserver(function (entries) {
        visible = entries[0].isIntersecting;
        syncTour();
      }, { threshold: 0.4 }).observe(showroom);
    }
    document.addEventListener('visibilitychange', syncTour);
  }

  // The terminal types its commands, and each answer appears after it, the first time it scrolls in. The
  // whole transcript is in the page from the start; only its visibility changes.

  function setupTerminal() {
    var term = document.getElementById('term');
    if (!term || !motionAllowed() || !('IntersectionObserver' in window)) return;
    var code = term.querySelector('code');
    if (!code) return;

    var parts = [];
    each(code.children, function (el) {
      if (el.classList.contains('t-cmd')) {
        var text = el.textContent;
        var chars = [];
        el.textContent = '';
        for (var i = 0; i < text.length; i++) {
          var ch = document.createElement('span');
          ch.className = 't-ch';
          ch.textContent = text.charAt(i);
          el.appendChild(ch);
          chars.push(ch);
        }
        parts.push({ chars: chars });
      } else {
        el.classList.add('t-hidden');
        parts.push({ el: el });
      }
    });
    term.classList.add('is-armed');

    function run() {
      var index = 0;

      function next() {
        if (index >= parts.length) return;
        var part = parts[index++];
        if (part.el) {
          part.el.classList.remove('t-hidden');
          window.setTimeout(next, part.el.classList.contains('t-prompt') ? 260 : 110);
          return;
        }
        var chars = part.chars;
        var delay = chars.length > 40 ? 9 : 36;
        var typed = 0;
        (function type() {
          if (typed > 0) chars[typed - 1].classList.remove('is-at');
          if (typed < chars.length) {
            chars[typed].classList.add('is-on', 'is-at');
            typed++;
            window.setTimeout(type, delay + Math.random() * delay);
          } else {
            window.setTimeout(next, 320);
          }
        })();
      }

      next();
    }

    onceVisible([term], 0.35, function () { window.setTimeout(run, 300); });
  }

  // The privacy statement lights up word by word as it scrolls toward the middle of the window.

  function setupStatement() {
    var statement = document.querySelector('.statement');
    if (!statement) return;
    var count = 0;

    each(Array.prototype.slice.call(statement.childNodes), function (node) {
      if (node.nodeType !== 3) return;
      var fragment = document.createDocumentFragment();
      node.textContent.split(/(\s+)/).forEach(function (piece) {
        if (!piece) return;
        if (/^\s+$/.test(piece)) {
          fragment.appendChild(document.createTextNode(piece));
          return;
        }
        var word = document.createElement('span');
        word.className = 'word';
        word.textContent = piece;
        word.style.setProperty('--i', String(count++));
        fragment.appendChild(word);
      });
      statement.replaceChild(fragment, node);
    });

    scenes.push(function (vh) {
      if (!motionAllowed()) {
        statement.style.setProperty('--lit', '999');
        return;
      }
      var top = statement.getBoundingClientRect().top;
      var start = vh * 0.9;
      var end = vh * 0.42;
      var progress = clamp((start - top) / (start - end), 0, 1);
      statement.style.setProperty('--lit', (progress * (count + 1)).toFixed(2));
    });
  }

  // The section links in the header follow along.

  function setupScrollspy() {
    if (!('IntersectionObserver' in window)) return;
    var links = {};
    each(document.querySelectorAll('.nav-extra a[href^="#"]'), function (link) {
      links[link.getAttribute('href').slice(1)] = link;
    });
    var observer = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        var link = links[entry.target.id];
        if (link) link.classList.toggle('is-current', entry.isIntersecting);
      });
    }, { rootMargin: '-45% 0px -50% 0px' });
    Object.keys(links).forEach(function (id) {
      var section = document.getElementById(id);
      if (section) observer.observe(section);
    });
  }

  syncMotion();
  if (reducedMotion.addEventListener) {
    reducedMotion.addEventListener('change', function () {
      syncMotion();
      requestUpdate();
    });
  }

  pickDownload();
  setupHeader();
  setupVideo();
  setupReveals();
  setupDemos();
  setupTour();
  setupThemes();
  setupTerminal();
  setupStatement();
  setupScrollspy();

  window.addEventListener('scroll', requestUpdate, { passive: true });
  window.addEventListener('resize', requestUpdate);
  runScenes();
})();
