
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
textSearch: 'contains'
});

window.grid = grid;
loadData();

function createClouds() {
  let cloudData = prepareCloudData();
  var data = cloudData.years.items.map(function (word) { return { x: word.text, percent: Math.round(word.weight * 100) / 100, value: word.count }; });

  var chart = anychart.tagCloud(data);
  chart.angles([0])

  var tooltip = chart.tooltip();
  tooltip.format("{%value} publicaciones");
  
  chart.listen("pointClick", function(e){
    filterYear(e.point.get("x"));
  });

  $('#years').empty();
  chart.container("years");
  chart.draw();

  data = cloudData.tags.items.map(function (word) { return { x: word.text, percent: Math.round(word.weight * 100) / 100, value: word.count }; });  
  data.sort((a, b) => b.value - a.value);
  data = data.slice(0, 50);
  chart = anychart.tagCloud(data);
  chart.angles([0])
  chart.textSpacing(5);
  //chart.scale(anychart.scales.log());

  var tooltip = chart.tooltip();
  tooltip.format("{%value} publicaciones");

  chart.listen("pointClick", function(e){
    filterTag(e.point.get("x"));
  });

  $('#tags').empty();
  chart.container("tags");
  chart.draw();
}

function updateClouds() {
  createClouds();
}

function prepareCloudData() {
  let years = {};
  let tags = {};

  data.forEach(item => {
    if (!window.tag || item.tags.includes(window.tag)) {
      years[item.year] = (years[item.year] || 0) + 1;
    }

    if (!window.year || item.year === window.year) {
      item.tags.forEach(tag => {
        tags[tag] = (tags[tag] || 0) + 1;
      });
    }
  });

  let filtered = data;
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
  var searches = [];
  if (tag) {
    searches.push({ field: 'tags', operator: 'contains', value: tag });
  }

  if (year) {
    searches.push({ field: 'year', operator: 'is', value: year });
  }

  grid.search(searches, 'AND');
  updateClouds();
}