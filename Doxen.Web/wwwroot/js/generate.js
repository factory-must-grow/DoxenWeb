// Экран /generate: переключение поля даты в текст и вставка диапазона
// из Excel в таблицу пакета (05-screens.md).
document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('.date-manual-toggle').forEach(function (link) {
        link.addEventListener('click', function (e) {
            e.preventDefault();
            var input = document.getElementById(link.getAttribute('data-target'));
            if (!input) {
                return;
            }

            if (input.type === 'date') {
                input.type = 'text';
                link.textContent = 'выбрать дату';
            } else {
                input.type = 'date';
                link.textContent = 'ввести вручную';
            }
        });
    });

    var table = document.getElementById('dataset-table');
    if (!table) {
        return;
    }

    table.addEventListener('paste', function (e) {
        var active = document.activeElement;
        if (!active || !active.classList.contains('dataset-cell')) {
            return;
        }

        var text = (e.clipboardData || window.clipboardData).getData('text');
        if (!text || (text.indexOf('\t') === -1 && text.indexOf('\n') === -1)) {
            return;
        }

        e.preventDefault();

        var rows = text.replace(/\r/g, '').split('\n').filter(function (r) { return r.length > 0; });
        var allRows = Array.prototype.slice.call(table.querySelectorAll('tbody tr'));
        var startRow = active.closest('tr');
        var startRowIndex = allRows.indexOf(startRow);
        var startCellIndex = Array.prototype.slice.call(startRow.querySelectorAll('td')).indexOf(active.closest('td'));

        var overflow = (startRowIndex + rows.length) > allRows.length;

        rows.forEach(function (rowText, rOffset) {
            var targetRow = allRows[startRowIndex + rOffset];
            if (!targetRow) {
                return;
            }

            var cells = rowText.split('\t');
            var targetCells = Array.prototype.slice.call(targetRow.querySelectorAll('td'));
            cells.forEach(function (cellText, cOffset) {
                var cell = targetCells[startCellIndex + cOffset];
                var input = cell && cell.querySelector('input');
                if (input) {
                    input.value = cellText.trim();
                }
            });
        });

        if (overflow) {
            window.alert('В таблице не хватило строк — часть данных не вставилась. ' +
                'Нажмите «Добавить ещё документ» нужное число раз и вставьте оставшееся.');
        }
    });
});
