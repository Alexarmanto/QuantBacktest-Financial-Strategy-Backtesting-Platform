// Train/test form (P4-X3): the heatmap form (optimize-form.js: strategy, axes, cell count, one submission with a
// spinner) plus the training share, an HTML number input whose [50, 90] range the browser checks. The server stays
// the authority on every rule.
import { initSweepForm } from './optimize-form.js';

const form = document.getElementById('validate-form');
if (form) {
    initSweepForm(form);
}
