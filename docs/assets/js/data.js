
let grid = new w2grid({
  name: 'grid',
  box: "#main",
  show: {
      footer: true,
      toolbar: true,
      lineNumbers : true
  },
  method: 'GET',
  columns: [
      { field: 'title', text: 'Titulo', size: '50%',  resizable: true, sortable: true, searchable: 'text',
          render: function(record) {
          return `<a href="${record.url}" target="_blank">${record.title}</a>`
          }
      },
      { field: 'tags', text: 'Temas', size: '35%',  resizable: true, sortable: true, searchable: 'text',
          render: function(record) {
          // render each tag as a span with the class 'tag'
          return record.tags.map(tag => `<span class="tag">${tag}</span>`).join(' ')
          }
      },
      { field: 'year', text: 'Año', size: '60px', sortable: true, searchable: 'int', style: 'text-align: center' }
  ],
  //fixedBody: false,
  recid: "id",
  statusRecordID: false,
  textSearch: 'contains',
  onClick(event) {
    console.log(event)
    event.done(() => {
        var sel = this.getSelection()
        if (sel.length == 1) {
          var id = sel[0]
          const modal = bootstrap.Modal.getInstance(document.getElementById('details'));
          $('#details-body').html('<div class="spinner-border text-warning" role="status"><span class="visually-hidden">Cargando...</span></div>');
          modal.show();

          fetch(`https://menosrelato.blob.core.windows.net/conicet/pubs/${id}.json`)
          .then(response => response.json())
          .then(json => {
            var html = window.renderDetails(json);
            $('#details-body').html(html);
          });
        }
    })
  }  
});

window.grid = grid;
loadData();

(function() {
  window.detailsTemplate = document.getElementById('details-template').innerHTML;
  window.renderDetails = Handlebars.compile(window.detailsTemplate);

  const detailsModal = bootstrap.Modal.getOrCreateInstance(document.getElementById('details'), { 
      keyboard: true,
      backdrop: true
  });
  
  document.addEventListener('keydown', function(event) {
    console.log(event);
      if (event.key === 'Escape' || event.keyCode === 27) {
          detailsModal.hide();
      }
  });  
})();

function createClouds() {
  let cloudData = prepareCloudData();
  var years = cloudData.years.items.map(function (word) { return { x: word.text, percent: Math.round(word.weight * 100) / 100, value: word.count }; });

  var chart = anychart.tagCloud(years);
  configureChart(chart);
  
  chart.listen("pointClick", function(e){
    filterYear(e.point.get("x"));
  });

  chart.container("years");
  chart.draw();

  var tags = cloudData.tags.items.map(function (word) { return { x: word.text, percent: Math.round(word.weight * 100) / 100, value: word.count }; });  
  tags.sort((a, b) => b.value - a.value);
  tags = tags.slice(0, 50);
  chart = anychart.tagCloud(tags);
  configureChart(chart);

  chart.listen("pointClick", function(e){
    filterTag(e.point.get("x"));
  });

  chart.container("tags");
  chart.draw();

  $('body').removeClass('busy');
}

function configureChart(chart) {
  chart.angles([0])
  chart.textSpacing(5);

  var tooltip = chart.tooltip();
  tooltip.format("{%value} publicaciones");
}

function prepareCloudData() {
  let years = {};
  let tags = {};

  window.data.forEach(item => {
    if (!window.tag || item.tags.includes(window.tag)) {
      years[item.year] = (years[item.year] || 0) + 1;
    }

    if (!window.year || item.year === window.year) {
      item.tags.forEach(tag => {
        tags[tag] = (tags[tag] || 0) + 1;
      });
    }
  });

  let filtered = window.data;
  if (window.year) {
    filtered = filtered.filter(item => item.year == window.year);
  }
  if (window.tag) {
    filtered = filtered.filter(item => item.tags.includes(window.tag));
  }
  let total = filtered.length;

  let yearArray = Object.entries(years).map(([text, count]) => {
    var word = { text, count, weight: (count / total) * 100, link: "#", handlers: {
      click: function() {
        filterYear(text);
    }}};

    if (window.year && text === window.year) {
      word.html = { 'selected': true };
    }

    return word;
  });

  let tagArray = Object.entries(tags).map(([text, count]) => {
    var word = { text, count, weight: (count / total) * 100, link: "#", handlers: {
      click: function() {
        filterTag(text);
    }}};

    if (window.tag && text == window.tag) {
      word.html = { 'selected': true };
    }

    return word;
  });

  var isBig = function(weight) { return weight >= 40 }; 
  var bigYear = yearArray.map(function (word) { return word.weight }).filter(isBig).length;
  var bigTag = tagArray.map(function (word) { return word.weight }).filter(isBig).length;

  return { 
    years: {
      items: yearArray,
      big: bigYear
    }, 
    tags: {
      items: tagArray,
      big: bigTag
    } 
  };
}

function filterYear(value) {
  window.year = parseInt(value);
  doSearch();
}

function filterTag(value) {
  window.tag = value;
  doSearch();
}

function doSearch() {
  $('#years').empty();
  $('#tags').empty();
  $('body').addClass('busy');

  setTimeout(() => {
    createClouds();
    var searches = [];
    if (tag) {
      searches.push({ field: 'tags', operator: 'contains', value: tag });
    }
  
    if (year) {
      searches.push({ field: 'year', operator: 'is', value: year });
    }
  
    window.grid.search(searches, 'AND');
  }, 200);
}