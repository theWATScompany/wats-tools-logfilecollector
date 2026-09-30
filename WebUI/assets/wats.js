/**
 * WATS UI Behaviour Library — wats.js
 * Version: 1.0
 *
 * Zero-dependency, ES-module compatible.
 * Provides: theme, sidebar, tabs, modal, toast, collapsible, chart theme.
 *
 * Usage (auto-init on DOMContentLoaded):
 *   <script type="module" src="wats.js"></script>
 *
 * Manual init:
 *   import { WatsUI } from './wats.js';
 *   WatsUI.init();
 */

const WatsUI = (() => {

  // ── Theme ───────────────────────────────────────────────────────────────────
  const THEME_KEY = 'wats-theme';

  function getTheme() {
    return localStorage.getItem(THEME_KEY) ||
      (window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark');
  }

  function applyTheme(theme) {
    document.documentElement.setAttribute('data-theme', theme);
    localStorage.setItem(THEME_KEY, theme);
    document.querySelectorAll('[data-theme-label]').forEach(el => {
      el.textContent = theme === 'dark' ? 'Light mode' : 'Dark mode';
    });
  }

  function toggleTheme() {
    applyTheme(getTheme() === 'dark' ? 'light' : 'dark');
  }

  // ── Sidebar ─────────────────────────────────────────────────────────────────
  function initSidebar() {
    document.querySelectorAll('[data-sidebar-toggle]').forEach(btn => {
      btn.addEventListener('click', () => {
        const targetId = btn.dataset.sidebarToggle || 'wats-sidebar';
        const sidebar = document.getElementById(targetId) ||
                        document.querySelector('.wats-sidebar');
        if (!sidebar) return;
        if (sidebar.classList.contains('hidden')) {
          sidebar.classList.remove('hidden');
        } else if (sidebar.classList.contains('collapsed')) {
          sidebar.classList.remove('collapsed');
        } else {
          sidebar.classList.add('collapsed');
        }
      });
    });
  }

  // ── Section → Tab mapping ────────────────────────────────────────────────────
  // Maps sidebar section names to the top-nav tab that owns them.
  const SECTION_TO_TAB = {
    dashboard: 'overview', summary: 'overview',
    buttons: 'components', forms: 'components', badges: 'components',
    cards: 'components', overlays: 'components',
    'chart-line': 'charts', 'chart-bar': 'charts', 'chart-donut': 'charts',
    datagrid: 'data', kpi: 'data',
    settings: 'settings',
  };

  function activateTab(tabName) {
    document.querySelectorAll('[data-tab][data-tab-group="main"]')
      .forEach(t => t.classList.toggle('active', t.dataset.tab === tabName));
  }

  // ── Tab navigation ──────────────────────────────────────────────────────────
  function initTabs() {
    document.querySelectorAll('[data-tab]').forEach(tab => {
      tab.addEventListener('click', () => {
        const group = tab.dataset.tabGroup || 'default';
        const target = tab.dataset.tab;

        // Deactivate all tabs and panels in same group
        document.querySelectorAll(`[data-tab][data-tab-group="${group}"]`)
          .forEach(t => t.classList.remove('active'));
        document.querySelectorAll(`[data-tab-panel][data-tab-group="${group}"]`)
          .forEach(p => p.style.display = 'none');

        // Handle ungrouped tabs
        if (!tab.dataset.tabGroup) {
          document.querySelectorAll('[data-tab]:not([data-tab-group])')
            .forEach(t => t.classList.remove('active'));
          document.querySelectorAll('[data-tab-panel]:not([data-tab-group])')
            .forEach(p => p.style.display = 'none');
        }

        tab.classList.add('active');

        // If this tab has a linked section, activate it via sidebar nav
        if (tab.dataset.tabSection) {
          const navItem = document.querySelector(
            `.wats-nav-item[data-section="${tab.dataset.tabSection}"]`);
          if (navItem) {
            navItem.click();
            return; // navItem.click() also activates the section display
          }
        }

        // Fallback: show/hide tab panels if they exist
        const panel = document.querySelector(`[data-tab-panel="${target}"]`);
        if (panel) panel.style.display = 'block';
      });
    });
  }

  // ── Sidebar nav items ────────────────────────────────────────────────────────
  function initNavItems() {
    document.querySelectorAll('.wats-nav-item[data-section]').forEach(item => {
      item.addEventListener('click', () => {
        const section = item.dataset.section;

        document.querySelectorAll('.wats-nav-item').forEach(i => i.classList.remove('active'));
        item.classList.add('active');

        document.querySelectorAll('.wats-section').forEach(s => s.style.display = 'none');
        const target = document.getElementById('section-' + section);
        if (target) target.style.display = 'block';

        // Sync the active top tab to match this section
        const tabName = SECTION_TO_TAB[section];
        if (tabName) activateTab(tabName);

        // Update page title if present
        const titleEl = document.getElementById('page-title');
        if (titleEl) titleEl.textContent = item.dataset.label || item.textContent.trim();
      });
    });
  }

  // ── Nav group collapse ───────────────────────────────────────────────────────
  function initNavGroups() {
    document.querySelectorAll('.wats-nav-group-header').forEach(header => {
      header.addEventListener('click', () => {
        header.closest('.wats-nav-group').classList.toggle('collapsed');
      });
    });
  }

  // ── Modal ────────────────────────────────────────────────────────────────────
  const modals = {};

  function openModal(id) {
    const backdrop = document.getElementById(id + '-backdrop') ||
                     document.querySelector(`[data-modal="${id}"]`);
    if (backdrop) backdrop.classList.add('open');
  }

  function closeModal(id) {
    const backdrop = document.getElementById(id + '-backdrop') ||
                     document.querySelector(`[data-modal="${id}"]`);
    if (backdrop) backdrop.classList.remove('open');
  }

  function initModals() {
    // Close on backdrop click
    document.querySelectorAll('.wats-modal-backdrop').forEach(backdrop => {
      backdrop.addEventListener('click', e => {
        if (e.target === backdrop) backdrop.classList.remove('open');
      });
    });

    // Open buttons
    document.querySelectorAll('[data-modal-open]').forEach(btn => {
      btn.addEventListener('click', () => openModal(btn.dataset.modalOpen));
    });

    // Close buttons
    document.querySelectorAll('[data-modal-close]').forEach(btn => {
      btn.addEventListener('click', () => closeModal(btn.dataset.modalClose));
    });

    // ESC key
    document.addEventListener('keydown', e => {
      if (e.key === 'Escape') {
        document.querySelectorAll('.wats-modal-backdrop.open')
          .forEach(b => b.classList.remove('open'));
      }
    });
  }

  // ── Toast ────────────────────────────────────────────────────────────────────
  let toastContainer = null;

  const TOAST_ICONS = {
    success: 'check_circle',
    error:   'error',
    warning: 'warning',
    info:    'info',
  };

  function showToast(message, type = 'info', duration = 4000) {
    if (!toastContainer) {
      toastContainer = document.createElement('div');
      toastContainer.className = 'wats-toast-container';
      document.body.appendChild(toastContainer);
    }

    const toast = document.createElement('div');
    toast.className = `wats-toast ${type}`;
    toast.innerHTML =
      `<span class="material-icons-round mi">${TOAST_ICONS[type] || 'notifications'}</span>` +
      `<span class="wats-toast-msg">${message}</span>`;
    toastContainer.appendChild(toast);

    // Auto-dismiss
    setTimeout(() => {
      toast.classList.add('hiding');
      setTimeout(() => toast.remove(), 200);
    }, duration);

    // Click to dismiss
    toast.addEventListener('click', () => {
      toast.classList.add('hiding');
      setTimeout(() => toast.remove(), 200);
    });
  }

  // ── Collapsible panels ───────────────────────────────────────────────────────
  function initCollapsibles() {
    document.querySelectorAll('.wats-collapsible-header').forEach(header => {
      header.addEventListener('click', () => {
        const panel = header.closest('.wats-collapsible');
        panel.classList.toggle('open');
      });
    });
    // Open ones with data-open attribute by default
    document.querySelectorAll('.wats-collapsible[data-open]').forEach(panel => {
      panel.classList.add('open');
    });
  }

  // ── Sidebar search filter ────────────────────────────────────────────────────
  function initSidebarSearch() {
    document.querySelectorAll('.wats-sidebar-search input').forEach(input => {
      input.addEventListener('input', () => {
        const q = input.value.toLowerCase();
        const sidebar = input.closest('.wats-sidebar');
        if (!sidebar) return;
        sidebar.querySelectorAll('.wats-nav-item').forEach(item => {
          const text = item.textContent.toLowerCase();
          item.style.display = !q || text.includes(q) ? '' : 'none';
        });
        // Show/hide group headers based on visible children
        sidebar.querySelectorAll('.wats-nav-group').forEach(group => {
          const anyVisible = [...group.querySelectorAll('.wats-nav-item')]
            .some(i => i.style.display !== 'none');
          group.style.display = anyVisible ? '' : 'none';
        });
      });
    });
  }

  // ── ECharts theme ────────────────────────────────────────────────────────────
  const CHART_PALETTE = [
    '#6d82a2','#776398','#8b607c','#ac636a',
    '#c16c5f','#d97b4c','#d18732','#99954b',
    '#509b8c','#62959e'
  ];

  function getEChartsThemeDark() {
    return {
      color: CHART_PALETTE,
      backgroundColor: 'transparent',
      textStyle: { color: '#eee', fontFamily: 'Inter,sans-serif', fontSize: 11 },
      legend: { textStyle: { color: '#c0c0c0' }, inactiveColor: '#555' },
      tooltip: {
        backgroundColor: '#303030',
        borderColor: '#181818',
        textStyle: { color: '#eee', fontSize: 11 },
        extraCssText: 'border-radius:5px;box-shadow:0 4px 16px rgba(0,0,0,.5)'
      },
      axisPointer: { lineStyle: { color: 'rgba(255,255,255,.2)' } },
      categoryAxis: {
        axisLine: { lineStyle: { color: 'rgba(255,255,255,.08)' } },
        axisTick: { lineStyle: { color: 'rgba(255,255,255,.08)' } },
        axisLabel: { color: '#888', fontSize: 10 },
        splitLine: { lineStyle: { color: 'rgba(255,255,255,.04)' } }
      },
      valueAxis: {
        axisLine: { show: false }, axisTick: { show: false },
        axisLabel: { color: '#888', fontSize: 10 },
        splitLine: { lineStyle: { color: 'rgba(255,255,255,.06)', type: 'dashed' } }
      },
      line: { smooth: true, symbolSize: 4, lineStyle: { width: 2 } },
      bar:  { barMaxWidth: 40, itemStyle: { borderRadius: [2, 2, 0, 0] } },
      pie:  { label: { color: '#c0c0c0', fontSize: 10 } }
    };
  }

  function getEChartsThemeLight() {
    return {
      color: CHART_PALETTE,
      backgroundColor: 'transparent',
      textStyle: { color: '#333', fontFamily: 'Inter,sans-serif', fontSize: 11 },
      legend: { textStyle: { color: '#555' }, inactiveColor: '#bbb' },
      tooltip: {
        backgroundColor: '#ffffff',
        borderColor: '#e0e0e0',
        textStyle: { color: '#333', fontSize: 11 },
        extraCssText: 'border-radius:5px;box-shadow:0 4px 12px rgba(0,0,0,.15)'
      },
      axisPointer: { lineStyle: { color: 'rgba(0,0,0,.15)' } },
      categoryAxis: {
        axisLine: { lineStyle: { color: 'rgba(0,0,0,.12)' } },
        axisTick: { lineStyle: { color: 'rgba(0,0,0,.12)' } },
        axisLabel: { color: '#888', fontSize: 10 },
        splitLine: { lineStyle: { color: 'rgba(0,0,0,.06)' } }
      },
      valueAxis: {
        axisLine: { show: false }, axisTick: { show: false },
        axisLabel: { color: '#888', fontSize: 10 },
        splitLine: { lineStyle: { color: 'rgba(0,0,0,.07)', type: 'dashed' } }
      },
      line: { smooth: true, symbolSize: 4, lineStyle: { width: 2 } },
      bar:  { barMaxWidth: 40, itemStyle: { borderRadius: [2, 2, 0, 0] } },
      pie:  { label: { color: '#555', fontSize: 10 } }
    };
  }

  function registerEChartsThemes() {
    if (typeof echarts === 'undefined') return;
    echarts.registerTheme('wats-dark',  getEChartsThemeDark());
    echarts.registerTheme('wats-light', getEChartsThemeLight());
  }

  function getEChartsTheme() {
    return document.documentElement.getAttribute('data-theme') === 'light'
      ? 'wats-light' : 'wats-dark';
  }

  // Re-render all echarts instances when theme changes
  function watchThemeForCharts() {
    const observer = new MutationObserver(() => {
      if (typeof echarts === 'undefined') return;
      // Destroy and re-init is complex; instead, update tooltip/text colors
      // For full re-render, callers can listen to 'wats:theme-change' event
      document.dispatchEvent(new CustomEvent('wats:theme-change', {
        detail: { theme: getTheme() }
      }));
    });
    observer.observe(document.documentElement, {
      attributes: true,
      attributeFilter: ['data-theme']
    });
  }

  // ── Page title inline edit ───────────────────────────────────────────────────
  function initInlineEdit() {
    document.querySelectorAll('[data-inline-edit]').forEach(el => {
      el.addEventListener('focus', () => el.select?.());
      el.addEventListener('keydown', e => {
        if (e.key === 'Enter') { e.preventDefault(); el.blur(); }
      });
    });
  }

  // ── Init all ─────────────────────────────────────────────────────────────────
  function init() {
    applyTheme(getTheme());
    initSidebar();
    initTabs();
    initNavItems();
    initNavGroups();
    initModals();
    initCollapsibles();
    initSidebarSearch();
    initInlineEdit();
    registerEChartsThemes();
    watchThemeForCharts();

    // Wire theme toggle buttons
    document.querySelectorAll('[data-theme-toggle]').forEach(btn => {
      btn.addEventListener('click', toggleTheme);
    });
  }

  // Auto-init
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }

  // Public API
  return {
    init,
    toggleTheme,
    applyTheme,
    getTheme,
    openModal,
    closeModal,
    showToast,
    getEChartsTheme,
    registerEChartsThemes,
    CHART_PALETTE,
  };
})();

// Also expose as global for non-module usage
if (typeof window !== 'undefined') window.WatsUI = WatsUI;
