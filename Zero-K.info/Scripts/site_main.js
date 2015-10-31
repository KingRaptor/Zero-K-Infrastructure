﻿(function ($) {
    /**
     * returns object with url variables
     * hash parameters take precedence over search
     * parameters
     * @return {Object}
     */
    $.getUrlVars = function () {
        var loc = document.location,
            search = loc.search.replace('?', '').split('&'),
            hash = loc.hash.replace('#', '').split('&'),
            i, l, v, vars = {};

        for (i = 0, l = search.length; i < l; i++) {
            v = search[i].split('=');
            vars[v[0]] = v[1];
        }

        for (i = 0, l = hash.length; i < l; i++) {
            v = hash[i].split('=');
            vars[v[0]] = v[1];
        }

        return vars;
    }

    /**
     * returns a single value from the getUrlVars hash
     * @param {String} i The hash index
     * @return {String}
     */
    $.getUrlVar = function (i) {
        return $.getUrlVars()[i];
    };
}(jQuery));


function DynDialog(url, title) {
    $.get(url, function(data) {
        var t = $("<div></div>");
        t.html(data);
        t.appendTo(document.body);
        GlobalPageInit(t);
        t.dialog(
        {
            autoOpen: true,
            show: "fade",
            hide: "fade",
            modal: false,
            title: title,
            width: 800,
            buttons: { "Close": function() { $(this).dialog("close"); } }
        });
    });
}

function ReplaceHistory(params) {
    // replace # is a hack for IE
    var res = jQuery.param.querystring(window.location.toString().replace("#", ""), params.replace("#", ""));
    res = res.replace(/%5B%5D=/g, "="); // hack for bbq adding extra [] to multiplied values
    window.History.replaceState(params, "", res);
    //var res = jQuery.param.querystring(window.location.toString(), params);
    //window.history.replaceState(params, "", res);
}

function SendLobbyCommand(link) {
    $.ajax({
        url: "/Lobby/SendCommand",
        data: {
            link: link
        },
        success: function(data) {
            if (data != null && data.length > 0) alert(data);
        },
        error: function() {
            alert("Error sending the command to lobby, please try again later");
        }
    });
}

var isBusy = false;
var ajaxScrollCount = 40;
var ajaxScrollOffset = 40;
var ajaxScrollEnabled = true;


function GlobalPageInit(root) {
    var s = root;
    if (s == null) s = $(document);

    s.find(".js_tabs").tabs({
        selected: parseInt($.getUrlVars().tab),
        ajaxOptions: {
            success: function(xhr, status, index, anchor) {
                $(document).find(".js_tabs").each(function() {
                    GlobalPageInit($(this));
                    ReplaceHistory("tab=" + $(this).tabs("option", "selected"));
                });
            }
        }
    });
    /*
    s.find(".js_tabs").each(function () {
        var tabval = $.getUrlVars().tab;
        if (tabval) {
            $(this).tabs("option", "selected",parseInt(tabval));

         //   $(this).tabs("load", parseInt($.getUrlVars().tab));
        }
    });*/


    s.find(".js_confirm").click(function() {
        var answer = confirm("Do you really want to do it?");
        return answer;
    });

    s.find(".js_dialog").dialog(
        {
            autoOpen: true,
            show: "fade",
            hide: "fade",
            modal: false,
            width: 800,
            buttons: { "Close": function() { $(this).dialog("close"); } }
        }
    );

    s.find(".js_datepicker").datepicker($.datepicker.regional["en"]);

    // buttonification
    s.find(":submit").button();
    s.find(":button").button();
    s.find(".js_button").button();
    s.find(".js_accordion").accordion();

    // selection for gird
    s.find(".js_selectrow").click(function() {
        var tr = $(this).closest("tr");
        if (tr.hasClass("row_selected"))
            tr.removeClass("row_selected");
        else
            tr.addClass("row_selected");
    });

    /* ajax form updater and scorll based loader
    It updates form on submit using ajax - sending offset 0 to it when user clicks

    It also updates form when user scrolls to bottom - sending current offset to it, this continues until controller returns data.

    busy element is made visible to display loading progress
    */
    var frm = s.find("#ajaxScrollForm");
    var prg = s.find("#busy");
    var target = s.find("#ajaxScrollTarget");

    if (frm && typeof frm.attr("id") != "undefined") {
        window.onscroll = function() {
            if (!ajaxScrollEnabled) {
                return;
            }
            var el = document.documentElement;
            var page = $("body");
            // bugfix for ebkit based stuff 
            // chrome needs scrolLTop out of jquery by the page. Object Opera, IE, Firefox take the original dom property
            //var scrollTop = el.scrollTop;
            //if (scrollTop == null || scrollTop == 0)
            var scrollTop = page.scrollTop();
            if (scrollTop == 0) scrollTop = el.scrollTop;
            if (el.scrollHeight - (scrollTop + el.clientHeight) < 50) {
                ajaxScrollEnabled = false;
                prg.show();
                $.post(frm.attr("action"), frm.serialize() + "&offset=" + ajaxScrollOffset, function(ret) {
                    target.append(ret);
                    ajaxScrollOffset = ajaxScrollOffset + ajaxScrollCount;
                    if (ret == "") ajaxScrollEnabled = false;
                    else ajaxScrollEnabled = true;
                    prg.hide();
                });
            }
        };

        frm.submit(function() {
            ajaxScrollEnabled = false;
            ajaxScrollOffset = 0;
            prg.show();
            $.post(frm.attr("action"), frm.serialize() + "&offset=" + ajaxScrollOffset, function(ret) {
                target.html(ret);
                ajaxScrollOffset = ajaxScrollCount;
                ajaxScrollEnabled = true;
                prg.hide();
                ReplaceHistory(frm.serialize());

            });
            return false;
        });
    }


    // img zoomer
    s.find("img.zoom").each(function() {
        $.data(this, "size", { width: $(this).width(), height: $(this).height() });
    }).hover(function() {
        $(this).stop().animate({ height: $.data(this, "size").height * 4, width: $.data(this, "size").width * 4 }, 300);
    }, function() {
        $(this).stop().animate({ height: $.data(this, "size").height, width: $.data(this, "size").width }, 600);
    });

    s.find(".js_expand").toggler({ speed: 0 });


    s.find("[data-preview]").each(function (i, trigger) {
        var name = $(trigger).data("preview");
        var txtSource = "[name='" + name + "']";
        $(trigger).click(function() {
            $.post("/Forum/Preview", {
                text: $(txtSource).val()
            }, function(data) {
                var dialogDiv = $("<div></div>");
                dialogDiv.html(data);
                dialogDiv.appendTo(document.body);
                GlobalPageInit(dialogDiv);
                dialogDiv.dialog(
                {
                    autoOpen: true,
                    show: "fade",
                    hide: "fade",
                    modal: false,
                    title: "Preview (keep open, auto refresh every 2s)",
                    width: 800,
                    open: function() {
                        $(trigger).hide();
                        var refresh = function () {
                            $.post("/Forum/Preview", {
                                text: $(txtSource).val()
                            },
                                function(d2) {
                                    dialogDiv.html(d2);
                                    GlobalPageInit(dialogDiv);
                                });
                            if (!$(trigger).is(":visible")) window.setTimeout(refresh, 2000);
                        };

                        window.setTimeout(refresh, 2000);
                    },
                    close: function() {
                        dialogDiv.detach();
                        $(trigger).show();
                    },
                    buttons: {
                        "Close": function() {
                            $(this).dialog("close");
                        }
                    }
                });

                

                event.preventDefault();
            });
        });
    });

    s.find("[data-autocomplete]").each(function(i, el) {
        var url = $(el).data("autocomplete");
        var action = $(el).data("autocomplete-action");

        $(el).autocomplete({
            minLength: 1,
            delay: 0,
            source: url,
            select: function (event, ui) {
                if (action === "submit") {
                    $(el).closest("form").submit();
                } else if (action === "goto") {
                    document.location = ui.item.url;
                }
            }
        }).data('ui-autocomplete')._renderItem = function (ul, item) {
            return $("<li></li>").data("item.autocomplete", item).append($("<a></a>").html(item.label)).appendTo(ul);
        };
    });

    SetupGrid(s);


    $(".qtip").remove(); // remove all floating tooltips
}


// setup busy indicator
$(function() {
    $(document).ajaxStart(function () {
        isBusy = true;
        setTimeout("if (isBusy) $('#busy').show('fade');", 500);
    });
    $(document).ajaxStop(function () {
        isBusy = false;
        $("#busy").hide();
    });
})