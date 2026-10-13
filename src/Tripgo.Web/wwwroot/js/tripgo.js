window.tripgo = {
    scrollToResults: function () {
        document.getElementById("trip-results")?.scrollIntoView({ behavior: "smooth", block: "start" });
    },
    goBack: function (fallbackUrl) {
        var origin = window.location.origin;
        var hasInternalReferrer = document.referrer && document.referrer.indexOf(origin) === 0;
        if (hasInternalReferrer && window.history && window.history.length > 1) {
            window.history.back();
        } else {
            window.location.href = fallbackUrl || "/";
        }
    }
};
