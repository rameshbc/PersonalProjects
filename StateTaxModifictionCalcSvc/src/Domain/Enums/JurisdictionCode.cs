namespace Domain.Enums;

public enum JurisdictionCode
{
    // Federal
    US,

    // States (alphabetical USPS codes)
    AL, AK, AZ, AR, CA, CO, CT, DE, FL, GA,
    HI, ID, IL, IN, IA, KS, KY, LA, ME, MD,
    MA, MI, MN, MS, MO, MT, NE, NV, NH, NJ,
    NM, NY, NC, ND, OH, OK, OR, PA, RI, SC,
    SD, TN, TX, UT, VT, VA, WA, WV, WI, WY,
    DC,

    // Local jurisdictions
    NYC,  // New York City
    RITA, // Regional Income Tax Agency (Ohio)
    CCA,  // Central Collection Agency (Ohio)
    PHIL, // Philadelphia
    KC    // Kansas City
}
