---
title: Conicet
order: 0
layout: default
---

# Conicet

Este sitio ofrece una visualización más intuitiva y accessible para explorar 
los temas de investigación del Conicet. 

> La fuente es tomada directamente de la [página oficial](https://ri.conicet.gov.ar/subject/).

## Areas Temáticas

{% assign pages = site.pages | sort: 'order' | sort: 'title' %}
{% for item in pages %}
    {% if item.url != page.url %}
* [{{  item.title  }}]({{ item.url | relative_url }})
    {% endif %}
{% endfor %}

## ¿Cómo funciona?

En la parte superior de cada area temática se encuentra una nube de palabras 
donde el tamaño de cada palabra es proporcional a la cantidad de publicaciones.

Es posible filtrar por año y por tema de investigación tocando directamente 
en el año o tema en cuestión en la nube de palabras en cada area. Al filtrar 
por tema, la nube de años se actualiza para mostrar los años en los que se 
publicaron trabajos sobre el tema seleccionado.

Consecuentemente, al filtrar por año, la nube de temas se actualiza para 
mostrar los temas sobre los que se publicaron trabajos en el año seleccionado.

La grilla en la parte inferior de cada area muestra los trabajos publicados 
con un enlace a la publicación original en el sitio del Conicet.

## Transparencia

Todo el proceso de extracción de datos y generación de este sitio está 
disponible como código abierto en [GitHub](https://github.com/MenosRelato/conicet) 
y puede ser ejecutado completamente local para replicar los datos directamente 
de la fuente.
